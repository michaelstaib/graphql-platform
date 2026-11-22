using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Results;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Types;
using HotChocolate.Fusion.Types.Metadata;
using HotChocolate.Language;
using HotChocolate.Language.Visitors;
using HotChocolate.Types;
using ThrowHelper = HotChocolate.Fusion.Execution.ThrowHelper;

namespace HotChocolate.Fusion.Planning;

public sealed partial class OperationPlanner
{
    private const string UploadScalarName = "Upload";
    private const string DynamicSchemaNameMarker = "__dynamic__";
    private const string UnlabelledBatchMark = "unlabelled";

    /// <summary>
    /// Converts the planner's intermediate plan steps into a final execution plan
    /// that the executor can run against the downstream source schemas.
    /// </summary>
    private OperationPlan BuildExecutionPlan(
        Operation operation,
        OperationDefinitionNode operationDefinition,
        ImmutableList<PlanStep> planSteps,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        ImmutableArray<IncrementalPlan> incrementalPlans,
        int searchSpace,
        int expandedNodes,
        int nextNodeId,
        PolicyPlanContext? policyContext,
        bool singlePlanDefer,
        CancellationToken cancellationToken)
    {
        if (operation.IsIntrospectionOnly())
        {
            var introspectionNode = new IntrospectionExecutionNode(
                1,
                [.. operation.RootSelectionSet.Selections],
                []);
            introspectionNode.Seal();

            var nodes = ImmutableArray.Create<ExecutionNode>(introspectionNode);

            var introspectionPlan = OperationPlan.Create(
                operation,
                nodes,
                nodes,
                deliveryGroups,
                incrementalPlans,
                PolicyPlan.Empty,
                searchSpace,
                expandedNodes);

            AssignIncrementalPlanAnchors(introspectionPlan, nodes, incrementalPlans);

            return introspectionPlan;
        }

        var ctx = new ExecutionPlanBuildContext(nextNodeId)
        {
            DeliveryGroups = singlePlanDefer ? deliveryGroups : default
        };
        var marker = policyContext is null
            ? null
            : PolicyMarker.TryCreate(_schema, policyContext, operation);

        var splitForEntityReuse = _options.EnableEntityReuseSplitting
            && operationDefinition.Operation is OperationType.Query
            && deliveryGroups.IsDefaultOrEmpty
            && incrementalPlans.IsDefaultOrEmpty;

        var requestCheck = ctx.DeliveryGroups.IsDefaultOrEmpty
            ? null
            : new RequestCheck(
                this,
                operationDefinition,
                marker,
                ctx.DeliveryGroups,
                nextNodeId,
                splitForEntityReuse,
                singlePlanDefer,
                cancellationToken);

        planSteps = TransformPlanSteps(planSteps, operationDefinition, marker, requestCheck);

        var hasVariables = HasVariables(operationDefinition, marker);

        if (policyContext is not null && marker is not null)
        {
            policyContext.AddRoot(marker);
        }

        if (requestCheck?.TryTakeBuild(planSteps) is { } checkedContext)
        {
            ctx = checkedContext;
        }
        else
        {
            BuildMergedExecutionNodes(
                planSteps,
                ctx,
                operationDefinition,
                hasVariables,
                splitForEntityReuse,
                singlePlanDefer,
                cancellationToken);
        }

        WireExecutionDependencies(ctx);

        var rootNodes = planSteps
            .Select(t => ResolveRedirectedStepId(t.Id, ctx.RedirectedStepIds))
            .Distinct()
            .Where(id => !ctx.DependenciesByStepId.ContainsKey(id) && ctx.ExecutionNodes.ContainsKey(id))
            .Select(id => ctx.ExecutionNodes[id])
            .ToImmutableArray();

        var allNodes = ctx.ExecutionNodes
            .OrderBy(t => t.Key)
            .Select(t => t.Value)
            .ToImmutableArray();

        if (operation.HasIntrospectionFields())
        {
            var introspectionNode = new IntrospectionExecutionNode(
                ctx.NextId(),
                operation.GetIntrospectionSelections(),
                []);
            rootNodes = rootNodes.Add(introspectionNode);
            allNodes = allNodes.Add(introspectionNode);
        }

        foreach (var node in allNodes)
        {
            node.Seal();
        }

        var operationPlan = OperationPlan.Create(
            operation,
            rootNodes,
            allNodes,
            deliveryGroups,
            incrementalPlans,
            policyContext?.CreatePlan() ?? PolicyPlan.Empty,
            searchSpace,
            expandedNodes);

        AssignIncrementalPlanAnchors(operationPlan, allNodes, incrementalPlans);

        return operationPlan;
    }

    private static bool HasVariables(OperationDefinitionNode operationDefinition, PolicyMarker? marker)
        => operationDefinition.VariableDefinitions.Count > 0 || marker is { HasVariables: true };

    // Builds the execution nodes of the plan steps and merges and batches them into the requests of the plan.
    private void BuildMergedExecutionNodes(
        ImmutableList<PlanStep> planSteps,
        ExecutionPlanBuildContext ctx,
        OperationDefinitionNode operationDefinition,
        bool hasVariables,
        bool splitForEntityReuse,
        bool singlePlanDefer,
        CancellationToken cancellationToken)
    {
        IndexDependencies(planSteps, ctx);
        BuildExecutionNodes(planSteps, ctx, _schema, hasVariables, cancellationToken);
        var serialScopes = operationDefinition.Operation is OperationType.Mutation
            ? CreateSerialScopes(planSteps, ctx)
            : null;

        if (singlePlanDefer)
        {
            MergeOperationsThatDifferInConditions(ctx, _options.MergePolicy, serialScopes);
        }

        MergeAndBatchOperations(
            ctx,
            _options.EnableRequestGrouping,
            _options.MergePolicy,
            splitForEntityReuse,
            serialScopes,
            _schema);
    }

    /// <summary>
    /// Builds the execution nodes of the step lists that the pass taking selections from other steps checks, at
    /// most <see cref="MaxBuilds"/> times, and keeps the build of the list the pass accepted last.
    /// </summary>
    private sealed class RequestCheck(
        OperationPlanner planner,
        OperationDefinitionNode operationDefinition,
        PolicyMarker? marker,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        int nextNodeId,
        bool splitForEntityReuse,
        bool singlePlanDefer,
        CancellationToken cancellationToken)
    {
        public const int MaxBuilds = 8;

        public ImmutableArray<DeliveryGroup> DeliveryGroups => deliveryGroups;

        public bool CanBuild => BuildCount < MaxBuilds;

        public int BuildCount { get; private set; }

        public StepBuild? Accepted { get; set; }

        public StepBuild Build(
            ImmutableList<PlanStep> planSteps,
            ForwardVariableRewriter.Context forwardVariableContext)
        {
            BuildCount++;

            var completed = CompletePlanSteps(planSteps, forwardVariableContext);
            var ctx = new ExecutionPlanBuildContext(nextNodeId) { DeliveryGroups = deliveryGroups };

            planner.BuildMergedExecutionNodes(
                completed,
                ctx,
                operationDefinition,
                HasVariables(operationDefinition, marker),
                splitForEntityReuse,
                singlePlanDefer,
                cancellationToken);

            return new StepBuild(planSteps, completed, ctx);
        }

        public ExecutionPlanBuildContext? TryTakeBuild(ImmutableList<PlanStep> completedSteps)
        {
            return Accepted is { } accepted && ReferenceEquals(accepted.Steps, completedSteps)
                ? accepted.Context
                : null;
        }
    }

    private sealed class StepBuild(
        ImmutableList<PlanStep> source,
        ImmutableList<PlanStep> steps,
        ExecutionPlanBuildContext context)
    {
        public ImmutableList<PlanStep> Source { get; } = source;

        public ImmutableList<PlanStep> Steps { get; } = steps;

        public ExecutionPlanBuildContext Context { get; } = context;
    }

    /// <summary>
    /// Assigns the stable id and the parent node id of every incremental plan after the
    /// root plan has been created. Nested incremental plans are associated with the plan
    /// that owns their parent delivery group.
    /// </summary>
    private static void AssignIncrementalPlanAnchors(
        OperationPlan operationPlan,
        ImmutableArray<ExecutionNode> allNodes,
        ImmutableArray<IncrementalPlan> incrementalPlans)
    {
        if (incrementalPlans.IsDefaultOrEmpty)
        {
            return;
        }

        // Plan-time id: the parent id is known here because the root plan
        // was just created, and OperationPlan.Create's content hash
        // does not include incremental plan ids.
        for (var i = 0; i < incrementalPlans.Length; i++)
        {
            var incrementalPlan = incrementalPlans[i];
            incrementalPlan.Id = $"{operationPlan.Id}#{i}";

            var path = ResolveIncrementalPlanPath(incrementalPlan);
            var parent = ResolveIncrementalPlanParent(incrementalPlan, incrementalPlans);
            var owningNodes = parent is null ? allNodes : parent.AllNodes;

            if (ResolveDeferParentNodeId(owningNodes, path) is { } parentNodeId)
            {
                incrementalPlan.ParentNodeId = parentNodeId;
            }
            else if (path.IsRoot && owningNodes.IsDefaultOrEmpty)
            {
                // The owning plan has no execution node, so the plan is anchored
                // directly at the operation root and no node id applies.
                incrementalPlan.ParentNodeId = IncrementalPlan.NoParentNodeId;
            }
            else
            {
                throw ThrowHelper.IncrementalPlanParentNotFound(path);
            }
        }
    }

    /// <summary>
    /// Returns the anchor path for the given incremental plan. When the plan
    /// has multiple delivery groups, the deepest delivery group path is used.
    /// </summary>
    private static SelectionPath ResolveIncrementalPlanPath(IncrementalPlan incrementalPlan)
    {
        SelectionPath? best = null;

        foreach (var usage in incrementalPlan.DeliveryGroups)
        {
            if (usage.Path is null)
            {
                continue;
            }

            if (best is null || usage.Path.Length > best.Length)
            {
                best = usage.Path;
            }
        }

        return best ?? SelectionPath.Root;
    }

    /// <summary>
    /// Finds the enclosing incremental plan for the given incremental plan.
    /// Returns <c>null</c> for top-level incremental plans.
    /// </summary>
    private static IncrementalPlan? ResolveIncrementalPlanParent(
        IncrementalPlan incrementalPlan,
        ImmutableArray<IncrementalPlan> incrementalPlans)
    {
        foreach (var usage in incrementalPlan.DeliveryGroups)
        {
            var ancestor = usage.Parent;
            while (ancestor is not null)
            {
                foreach (var candidate in incrementalPlans)
                {
                    if (ReferenceEquals(candidate, incrementalPlan))
                    {
                        continue;
                    }

                    foreach (var candidateUsage in candidate.DeliveryGroups)
                    {
                        if (ReferenceEquals(candidateUsage, ancestor))
                        {
                            return candidate;
                        }
                    }
                }

                ancestor = ancestor.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the execution node in <paramref name="owningNodes"/> that produces the
    /// result object the defer is anchored in. The match is the node with the deepest
    /// fetch target that is an ancestor of (or equal to) <paramref name="deferPath"/>
    /// and that resolves the field the defer path descends into below that target.
    /// </summary>
    private static int? ResolveDeferParentNodeId(
        ImmutableArray<ExecutionNode> owningNodes,
        SelectionPath deferPath)
    {
        int? match = null;
        var bestDepth = -1;
        int? fallbackMatch = null;
        var fallbackDepth = -1;
        int? introspectionMatch = null;

        for (var i = 0; i < owningNodes.Length; i++)
        {
            SelectionPath target;
            ResultSelectionSet resultSelectionSet;

            switch (owningNodes[i])
            {
                case IntrospectionExecutionNode when deferPath.IsRoot:
                    // Introspection results live in the root result object, so a root
                    // defer can anchor there when no fetch node produces it.
                    introspectionMatch ??= owningNodes[i].Id;
                    continue;

                case OperationExecutionNode op:
                    target = op.Target;
                    resultSelectionSet = op.ResultSelectionSet;
                    break;

                case ApolloOperationExecutionNode apolloOp:
                    target = apolloOp.Target;
                    resultSelectionSet = apolloOp.ResultSelectionSet;
                    break;

                default:
                    continue;
            }

            if (!target.IsParentOfOrSame(deferPath))
            {
                continue;
            }

            // Several nodes can share the same target (all root fetch nodes target `$`),
            // but only the node that actually resolves the field the defer path descends
            // into produces the enclosing result object the deferred fields merge into.
            // A node that only shares an ancestor path is kept as a fallback in case no
            // node resolves the anchor field itself (e.g. it is fetched by a batch node).
            if (!ResolvesDeferAnchorField(resultSelectionSet, deferPath, target.Length))
            {
                if (target.Length > fallbackDepth)
                {
                    fallbackMatch = owningNodes[i].Id;
                    fallbackDepth = target.Length;
                }

                continue;
            }

            // Pick the deepest matching node so we attach to the most specific
            // fetch (e.g. a lookup node at $.user rather than a root fetch) when
            // multiple nodes could claim the defer's anchor.
            if (target.Length > bestDepth)
            {
                match = owningNodes[i].Id;
                bestDepth = target.Length;
            }
        }

        return match ?? fallbackMatch ?? introspectionMatch;
    }

    /// <summary>
    /// Checks whether the node owning <paramref name="resultSelectionSet"/> resolves
    /// the field through which <paramref name="deferPath"/> descends below the node's
    /// fetch target. When the defer path does not descend through a field (it is
    /// anchored directly in the node's own result object), the node qualifies.
    /// </summary>
    private static bool ResolvesDeferAnchorField(
        ResultSelectionSet resultSelectionSet,
        SelectionPath deferPath,
        int targetLength)
    {
        for (var i = targetLength; i < deferPath.Length; i++)
        {
            var segment = deferPath[i];

            // Inline fragment segments stay on the same result object, so the first
            // field segment below the target decides which node owns the anchor.
            if (segment.Kind is not SelectionPathSegmentKind.Field)
            {
                continue;
            }

            foreach (var responseName in resultSelectionSet.ResponseNames)
            {
                if (string.Equals(responseName, segment.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Counts the operation steps that remain after <see cref="TransformPlanSteps"/>, using
    /// <see cref="TryTransformOperationStep"/> and <see cref="RemoveKeyOnlyLookupSteps"/>.
    /// </summary>
    private static int CountSurvivingOperationSteps(ImmutableList<PlanStep> planSteps)
    {
        var remaining = ImmutableList.CreateBuilder<PlanStep>();

        foreach (var step in planSteps)
        {
            if (step is OperationPlanStep operationPlanStep)
            {
                if (TryTransformOperationStep(operationPlanStep, out var transformed))
                {
                    remaining.Add(transformed);
                }
            }
            else
            {
                remaining.Add(step);
            }
        }

        var count = 0;

        foreach (var step in RemoveKeyOnlyLookupSteps(remaining.ToImmutable()))
        {
            if (step is OperationPlanStep)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsEmptyOperation(OperationPlanStep step)
    {
        if (step.Definition.SelectionSet.Selections.Count == 0)
        {
            return true;
        }

        return step.Definition.SelectionSet.Selections is
        [
#pragma warning disable format
            FieldNode
            {
                Alias: null,
                Name.Value: IntrospectionFieldNames.TypeName,
                Directives: [{ Name.Value: "fusion__empty" }]
            }
#pragma warning restore format
        ];
    }

    private static OperationPlanStep RemoveEmptySelectionSets(OperationPlanStep step)
    {
        var updatedDefinition = RemoveEmptySelections(step.Definition);
        return ReferenceEquals(updatedDefinition, step.Definition)
            ? step
            : step with { Definition = updatedDefinition };
    }

    /// <summary>
    /// Applies the step-level transforms shared by <see cref="TransformPlanSteps"/> and
    /// <see cref="CountSurvivingOperationSteps"/>. Returns <c>false</c> when the step has no
    /// meaningful selections left and must be discarded.
    /// </summary>
    private static bool TryTransformOperationStep(
        OperationPlanStep step,
        out OperationPlanStep transformed)
    {
        // Requirement rewriting can leave behind empty child selection sets.
        // We remove them here so later stages do not treat them as real selections.
        step = RemoveEmptySelectionSets(step);

        // Discard steps that have no meaningful selections left.
        if (IsEmptyOperation(step))
        {
            transformed = step;
            return false;
        }

        // When a @skip or @include directive gates every selection of the
        // operation, we promote it to a node-level condition. This lets the
        // executor skip the entire network call when the condition is not
        // met, rather than sending a request that returns nothing.
        // Directives that gate only some selections stay in the document and
        // are evaluated by the source schema.
        if (TryExtractCommonConditionsAndRewrite(step, out var updated))
        {
            step = updated;
        }

        // Strip @defer directives from subgraph operations. The gateway
        // manages deferral itself and subgraphs should not see @defer.
        transformed = StripDeferDirectivesFromStep(step);
        return true;
    }

    private static OperationPlanStep StripDeferDirectivesFromStep(OperationPlanStep step)
    {
        var updated = StripDeferFromSelectionSet(step.Definition.SelectionSet);

        if (ReferenceEquals(updated, step.Definition.SelectionSet))
        {
            return step;
        }

        return step with { Definition = step.Definition.WithSelectionSet(updated) };
    }

    private static SelectionSetNode StripDeferFromSelectionSet(SelectionSetNode selectionSet)
    {
        List<ISelectionNode>? rewritten = null;
        List<int>? removed = null;

        for (var i = 0; i < selectionSet.Selections.Count; i++)
        {
            var selection = selectionSet.Selections[i];

            if (selection is InlineFragmentNode inlineFragment)
            {
                var strippedDirectives = StripDeferDirective(inlineFragment.Directives);
                var strippedInner = StripDeferFromSelectionSet(inlineFragment.SelectionSet);

                if (!ReferenceEquals(strippedDirectives, inlineFragment.Directives)
                    || !ReferenceEquals(strippedInner, inlineFragment.SelectionSet))
                {
                    rewritten ??= [.. selectionSet.Selections];
                    rewritten[i] = inlineFragment
                        .WithDirectives(strippedDirectives)
                        .WithSelectionSet(strippedInner);

                    // A fragment that only held a deferred fragment, which a lookup resolves,
                    // has nothing left to select.
                    if (IsPlaceholderOnly(strippedInner))
                    {
                        removed ??= [];
                        removed.Add(i);
                    }
                }
            }
            else if (selection is FieldNode { SelectionSet: not null } field)
            {
                var strippedInner = StripDeferFromSelectionSet(field.SelectionSet);

                if (!ReferenceEquals(strippedInner, field.SelectionSet))
                {
                    rewritten ??= [.. selectionSet.Selections];
                    rewritten[i] = field.WithSelectionSet(strippedInner);
                }
            }
        }

        if (rewritten is null)
        {
            return selectionSet;
        }

        if (removed is not null)
        {
            for (var i = removed.Count - 1; i >= 0; i--)
            {
                rewritten.RemoveAt(removed[i]);
            }
        }

        return new SelectionSetNode(rewritten);
    }

    private static bool IsPlaceholderOnly(SelectionSetNode selectionSet)
    {
        if (selectionSet.Selections.Count == 0)
        {
            return true;
        }

        return selectionSet.Selections is [FieldNode { Alias: null } field]
            && field.Name.Value.Equals(IntrospectionFieldNames.TypeName, StringComparison.Ordinal)
            && field.Directives is [{ Name.Value: "fusion__empty" }];
    }

    private static IReadOnlyList<DirectiveNode> StripDeferDirective(IReadOnlyList<DirectiveNode> directives)
    {
        for (var i = 0; i < directives.Count; i++)
        {
            if (directives[i].Name.Value.Equals(
                DirectiveNames.Defer.Name,
                StringComparison.Ordinal))
            {
                var result = new List<DirectiveNode>(directives.Count - 1);

                for (var j = 0; j < directives.Count; j++)
                {
                    if (j != i)
                    {
                        result.Add(directives[j]);
                    }
                }

                return result;
            }
        }

        return directives;
    }

    private static ImmutableList<PlanStep> TransformPlanSteps(
        ImmutableList<PlanStep> planSteps,
        OperationDefinitionNode originalOperation,
        PolicyMarker? marker,
        RequestCheck? requestCheck)
    {
        var forwardVariableContext = new ForwardVariableRewriter.Context();

        foreach (var variableDef in originalOperation.VariableDefinitions)
        {
            forwardVariableContext.Variables[variableDef.Variable.Name.Value] = variableDef;
        }

        if (marker is not null)
        {
            planSteps = marker.Mark(planSteps);

            foreach (var variableDef in marker.GetVariableDefinitions())
            {
                forwardVariableContext.Variables[variableDef.Variable.Name.Value] = variableDef;
            }
        }

        var updatedPlanSteps = planSteps;
        var hasDeliveryGroups = requestCheck is not null;

        foreach (var step in planSteps)
        {
            if (step is not OperationPlanStep operationPlanStep)
            {
                continue;
            }

            if (!TryTransformOperationStep(operationPlanStep, out var transformed))
            {
                updatedPlanSteps = updatedPlanSteps.Remove(step);
                continue;
            }

            // Attach variable definitions so the operation is syntactically valid
            // when sent to the downstream service. A plan with delivery groups attaches them
            // after the pass that takes selections from other steps.
            updatedPlanSteps = updatedPlanSteps.Replace(
                step,
                hasDeliveryGroups ? transformed : AddVariableDefinitions(transformed, forwardVariableContext));
        }

        if (requestCheck is not null)
        {
            updatedPlanSteps = TakeSelectionsFromHolders(
                updatedPlanSteps,
                originalOperation,
                requestCheck,
                forwardVariableContext);

            // The pass leaves the build of the list it ends with in the check, unless it changed nothing.
            return requestCheck.Accepted is { } accepted && ReferenceEquals(accepted.Source, updatedPlanSteps)
                ? accepted.Steps
                : CompletePlanSteps(updatedPlanSteps, forwardVariableContext);
        }

        return RemoveKeyOnlyLookupSteps(updatedPlanSteps);
    }

    // Attaches the variable definitions to the steps of a plan with delivery groups and removes the lookup steps
    // that select nothing but their entry key.
    private static ImmutableList<PlanStep> CompletePlanSteps(
        ImmutableList<PlanStep> planSteps,
        ForwardVariableRewriter.Context forwardVariableContext)
    {
        var updatedPlanSteps = planSteps;

        foreach (var step in planSteps)
        {
            if (step is OperationPlanStep operationPlanStep)
            {
                updatedPlanSteps = updatedPlanSteps.Replace(
                    step,
                    AddVariableDefinitions(operationPlanStep, forwardVariableContext));
            }
        }

        return RemoveKeyOnlyLookupSteps(updatedPlanSteps);
    }

    private static OperationPlanStep AddVariableDefinitions(
        OperationPlanStep step,
        ForwardVariableRewriter.Context forwardVariableContext)
    {
        forwardVariableContext.Reset();

        foreach (var (key, requirement) in step.Requirements.OrderBy(t => t.Key))
        {
            forwardVariableContext.Requirements[key] =
                new VariableDefinitionNode(
                    null,
                    new VariableNode(null, new NameNode(key)),
                    description: null,
                    requirement.Type,
                    null,
                    []);
        }

        var rewrittenNode = s_forwardVariableRewriter.Rewrite(step.Definition, forwardVariableContext);

        if (rewrittenNode is OperationDefinitionNode rewrittenOperationNode
            && !ReferenceEquals(rewrittenOperationNode, step.Definition))
        {
            return step with { Definition = rewrittenOperationNode };
        }

        return step;
    }

    /// <summary>
    /// Removes lookup steps that select nothing but the entry key a step they depend on already
    /// selects, and makes their dependents depend on the steps the removed step depended on.
    /// </summary>
    private static ImmutableList<PlanStep> RemoveKeyOnlyLookupSteps(ImmutableList<PlanStep> planSteps)
    {
        var removed = true;

        while (removed)
        {
            removed = false;

            for (var i = 0; i < planSteps.Count; i++)
            {
                if (planSteps[i] is not OperationPlanStep { Lookup: not null } candidate
                    || !IsKeyOnlyLookup(candidate)
                    || IsReferencedByNodeStep(planSteps, candidate.Id)
                    || !TryGetKeyProducers(planSteps, candidate, out var producerIndexes))
                {
                    continue;
                }

                foreach (var producerIndex in producerIndexes)
                {
                    var producer = (OperationPlanStep)planSteps[producerIndex];

                    planSteps = planSteps.SetItem(
                        producerIndex,
                        producer with
                        {
                            Dependents = producer.Dependents.Remove(candidate.Id).Union(candidate.Dependents)
                        });
                }

                planSteps = planSteps.RemoveAt(i);
                removed = true;
                break;
            }
        }

        return planSteps;

        static bool IsKeyOnlyLookup(OperationPlanStep step)
        {
            return step.Definition.SelectionSet.Selections is
                [FieldNode { Alias: null, SelectionSet: { } entrySelectionSet }]
                && SyntaxComparer.BySyntax.Equals(entrySelectionSet, step.Lookup!.Requirements);
        }

        static bool TryGetKeyProducers(
            ImmutableList<PlanStep> planSteps,
            OperationPlanStep candidate,
            out List<int> producerIndexes)
        {
            producerIndexes = [];
            var providesKey = false;

            for (var i = 0; i < planSteps.Count; i++)
            {
                if (planSteps[i] is not OperationPlanStep producer
                    || !producer.Dependents.Contains(candidate.Id))
                {
                    continue;
                }

                producerIndexes.Add(i);

                if (producer.Target.IsParentOfOrSame(candidate.Target)
                    && ContainsSelectionsAtPath(
                        GetEntrySelectionSet(producer),
                        candidate.Target.RelativeTo(producer.Target),
                        candidate.Lookup!.Requirements))
                {
                    providesKey = true;
                }
            }

            return providesKey;
        }
    }

    private static bool IsReferencedByNodeStep(ImmutableList<PlanStep> planSteps, int stepId)
    {
        foreach (var step in planSteps)
        {
            if (step is not NodeFieldPlanStep nodeStep)
            {
                continue;
            }

            if (nodeStep.FallbackQuery.Id == stepId)
            {
                return true;
            }

            foreach (var branch in nodeStep.Branches.Values)
            {
                if (branch.Id == stepId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SelectionSetNode GetEntrySelectionSet(OperationPlanStep step)
    {
        return step.Lookup is not null
            && step.Definition.SelectionSet.Selections is [FieldNode { SelectionSet: { } entrySelectionSet }]
                ? entrySelectionSet
                : step.Definition.SelectionSet;
    }

    /// <summary>
    /// Removes the entry selections of the lookup steps of a plan with delivery groups that another step fetches at
    /// the same response path whenever the step runs, and makes the dependents of the step depend on that step.
    /// A step without delivery groups only gives up the entry key that its producer fetches. A step keeps the fields
    /// that the operation selects for its groups, and no source schema gets more requests than the plan build
    /// creates for the steps before the pass.
    /// </summary>
    private static ImmutableList<PlanStep> TakeSelectionsFromHolders(
        ImmutableList<PlanStep> planSteps,
        OperationDefinitionNode operation,
        RequestCheck requestCheck,
        ForwardVariableRewriter.Context forwardVariableContext)
    {
        using var pass = new HolderPass(planSteps, operation, requestCheck, forwardVariableContext);
        var taken = TakeSelections(planSteps, pass, checkEveryStep: false);

        if (ReferenceEquals(taken, planSteps) || pass.TryAccept(planSteps, taken))
        {
            return taken;
        }

        // The result sends a source more requests, so the steps are taken again with a check per step.
        return TakeSelections(planSteps, pass, checkEveryStep: true);
    }

    private static ImmutableList<PlanStep> TakeSelections(
        ImmutableList<PlanStep> planSteps,
        HolderPass pass,
        bool checkEveryStep)
    {
        for (var i = 0; i < planSteps.Count; i++)
        {
            if (planSteps[i] is not OperationPlanStep { Lookup.Path.Length: 0 } step
                || step.Definition.SelectionSet.Selections is not [FieldNode { SelectionSet: { } entry } entryField]
                || IsReferencedByNodeStep(planSteps, step.Id))
            {
                continue;
            }

            var hasGroups = !step.DeliveryGroups.IsDefaultOrEmpty;
            pass.Begin(planSteps, step);

            for (var j = 0; j < entry.Selections.Count; j++)
            {
                var selection = entry.Selections[j];

                if (selection is FieldNode field
                    && !field.Name.Value.Equals(IntrospectionFieldNames.TypeName, StringComparison.Ordinal)
                    && (hasGroups || IsEntryKey(step, field))
                    && pass.TryFindHolder(
                        planSteps,
                        step,
                        field,
                        onlyProducers: !hasGroups,
                        delivered: hasGroups && pass.IsSelectedForGroups(step, field),
                        out var holderId))
                {
                    pass.AddHolder(holderId);
                }
                else
                {
                    pass.Keep(selection);
                }
            }

            if (pass.HolderCount == 0
                || (pass.KeptCount == 0 && (!hasGroups || !HasAnotherStepPerGroup(planSteps, step))))
            {
                continue;
            }

            var updated = step;

            if (pass.KeptCount > 0)
            {
                updated = step with
                {
                    Definition = step.Definition.WithSelectionSet(
                        new SelectionSetNode(
                            [entryField.WithSelectionSet(new SelectionSetNode(pass.GetKeptSelections()))])),
                    ConditionAlternatives = []
                };

                if (TryExtractCommonConditionsAndRewrite(updated, out var rewritten))
                {
                    updated = rewritten;
                }
                else if (!hasGroups)
                {
                    continue;
                }

                if (!pass.DependentsHoldPromotedConditions(step, updated))
                {
                    continue;
                }
            }

            if (!hasGroups)
            {
                var plainCandidate = planSteps.SetItem(i, updated);

                if (checkEveryStep && !pass.TryAccept(planSteps, plainCandidate))
                {
                    continue;
                }

                planSteps = plainCandidate;
                continue;
            }

            var candidate = planSteps;

            for (var j = 0; j < candidate.Count; j++)
            {
                if (candidate[j] is not OperationPlanStep other)
                {
                    continue;
                }

                var dependents = other.Dependents;

                if (pass.IsHolder(other.Id))
                {
                    dependents = dependents.Union(step.Dependents);
                }

                if (pass.KeptCount == 0)
                {
                    dependents = dependents.Remove(step.Id);
                }

                if (!ReferenceEquals(dependents, other.Dependents))
                {
                    candidate = candidate.SetItem(j, other with { Dependents = dependents });
                }
            }

            candidate = pass.KeptCount == 0 ? candidate.RemoveAt(i) : candidate.SetItem(i, updated);

            if (checkEveryStep && !pass.TryAccept(planSteps, candidate))
            {
                continue;
            }

            planSteps = candidate;

            if (pass.KeptCount == 0)
            {
                i--;
            }
        }

        return planSteps;
    }

    private static bool IsEntryKey(OperationPlanStep step, FieldNode field)
    {
        if (field.Directives.Count > 0)
        {
            return false;
        }

        var requirements = step.Lookup!.Requirements.Selections;

        for (var i = 0; i < requirements.Count; i++)
        {
            if (SyntaxComparer.BySyntax.Equals(requirements[i], field))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnotherStepPerGroup(ImmutableList<PlanStep> planSteps, OperationPlanStep step)
    {
        foreach (var group in step.DeliveryGroups)
        {
            var found = false;

            foreach (var planStep in planSteps)
            {
                if (planStep is OperationPlanStep { DeliveryGroups.IsDefaultOrEmpty: false } other
                    && other.Id != step.Id
                    && other.DeliveryGroups.Contains(group))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct ConditionKey(string VariableName, bool PassingValue);

    /// <summary>
    /// Finds the steps that hold the entry selections of a lookup step and checks that taking them does not
    /// send a source schema more requests. The buffers are rented once for the pass.
    /// </summary>
    private sealed class HolderPass : IDisposable
    {
        private const int ConditionCapacity = 64;

        private readonly OperationDefinitionNode _operation;
        private readonly ImmutableArray<DeliveryGroup> _deliveryGroups;
        private readonly RequestCheck _requestCheck;
        private readonly ForwardVariableRewriter.Context _forwardVariableContext;
        private readonly int _maxId;
        private readonly int _schemaCapacity;
        private readonly string?[] _schemaNames;
        private readonly ConditionKey[] _allowed;
        private readonly ISelectionNode[] _kept;
        private readonly int[] _holders;
        private readonly OperationPlanStep?[] _byId;
        private readonly int[] _depth;
        private readonly int[] _remaining;
        private readonly int[] _queue;
        private readonly int[] _visited;
        private int[] _acceptedCounts;
        private int[] _candidateCounts;
        private ImmutableArray<int> _stepGroups;
        private int _schemaCount;
        private int _allowedCount;
        private int _visitStamp;

        public HolderPass(
            ImmutableList<PlanStep> planSteps,
            OperationDefinitionNode operation,
            RequestCheck requestCheck,
            ForwardVariableRewriter.Context forwardVariableContext)
        {
            _requestCheck = requestCheck;
            _forwardVariableContext = forwardVariableContext;
            var maxId = 0;
            var maxEntry = 0;

            foreach (var planStep in planSteps)
            {
                maxId = Math.Max(maxId, planStep.Id);

                if (planStep is OperationPlanStep { Lookup: not null } step
                    && step.Definition.SelectionSet.Selections is [FieldNode { SelectionSet: { } entry }])
                {
                    maxEntry = Math.Max(maxEntry, entry.Selections.Count);
                }
            }

            _operation = operation;
            _deliveryGroups = requestCheck.DeliveryGroups;
            _maxId = maxId;
            _schemaCapacity = planSteps.Count + 1;
            _schemaNames = ArrayPool<string?>.Shared.Rent(_schemaCapacity);
            _acceptedCounts = ArrayPool<int>.Shared.Rent(_schemaCapacity);
            _candidateCounts = ArrayPool<int>.Shared.Rent(_schemaCapacity);
            _allowed = ArrayPool<ConditionKey>.Shared.Rent(ConditionCapacity);
            _kept = ArrayPool<ISelectionNode>.Shared.Rent(Math.Max(maxEntry, 1));
            _holders = ArrayPool<int>.Shared.Rent(Math.Max(maxEntry, 1));
            _byId = ArrayPool<OperationPlanStep?>.Shared.Rent(maxId + 1);
            _depth = ArrayPool<int>.Shared.Rent(maxId + 1);
            _remaining = ArrayPool<int>.Shared.Rent(maxId + 1);
            _queue = ArrayPool<int>.Shared.Rent(maxId + 1);
            _visited = ArrayPool<int>.Shared.Rent(maxId + 1);
            _visited.AsSpan(0, maxId + 1).Clear();
        }

        public int KeptCount { get; private set; }

        public int HolderCount { get; private set; }

        public void Begin(ImmutableList<PlanStep> planSteps, OperationPlanStep step)
        {
            Index(planSteps, _byId, _depth);
            KeptCount = 0;
            HolderCount = 0;
            _allowedCount = 0;

            foreach (var condition in step.Conditions)
            {
                if (_allowedCount < _allowed.Length)
                {
                    _allowed[_allowedCount++] = new ConditionKey(condition.VariableName, condition.PassingValue);
                }
            }
        }

        public void Keep(ISelectionNode selection) => _kept[KeptCount++] = selection;

        public ISelectionNode[] GetKeptSelections() => _kept.AsSpan(0, KeptCount).ToArray();

        public void AddHolder(int holderId)
        {
            if (!IsHolder(holderId))
            {
                _holders[HolderCount++] = holderId;
            }
        }

        public bool IsHolder(int stepId) => Array.IndexOf(_holders, stepId, 0, HolderCount) >= 0;

        // Checks that the operation selects the field, or a leaf below it, at the target of the step inside one of
        // the delivery groups of the step.
        public bool IsSelectedForGroups(OperationPlanStep step, FieldNode field)
        {
            _stepGroups = step.DeliveryGroups;
            return IsSelectedAt(_operation.SelectionSet, step.Target, 0, -1, field);
        }

        public bool TryFindHolder(
            ImmutableList<PlanStep> planSteps,
            OperationPlanStep step,
            FieldNode field,
            bool onlyProducers,
            bool delivered,
            out int holderId)
        {
            var restoreCount = _allowedCount;
            OperationPlanStep? best = null;

            if (TryPush(field.Directives))
            {
                foreach (var planStep in planSteps)
                {
                    if (planStep is not OperationPlanStep holder
                        || holder.Id == step.Id
                        || (onlyProducers && !holder.Dependents.Contains(step.Id))
                        || holder.Lookup is { Path.Length: > 0 }
                        || !holder.Target.IsParentOfOrSame(step.Target)
                        || !CoversEveryDeliveryGroup(step.DeliveryGroups, holder.DeliveryGroups)
                        || (delivered && !HasEveryDeliveryGroup(step.DeliveryGroups, holder.DeliveryGroups))
                        || !AreConditionsAllowed(holder)
                        || !FetchesAtPath(GetEntrySelectionSet(holder), step.Target, holder.Target.Length, field)
                        || !CoversDependents(step, holder)
                        || IsReachable(step.Id, holder.Id))
                    {
                        continue;
                    }

                    if (best is null || IsEarlier(holder, best))
                    {
                        best = holder;
                    }
                }
            }

            _allowedCount = restoreCount;
            holderId = best?.Id ?? -1;
            return best is not null;
        }

        public bool DependentsHoldPromotedConditions(OperationPlanStep step, OperationPlanStep updated)
        {
            foreach (var dependentId in step.Dependents)
            {
                if (dependentId > _maxId || _byId[dependentId] is not { } dependent)
                {
                    continue;
                }

                foreach (var condition in updated.Conditions)
                {
                    if (Array.IndexOf(step.Conditions, condition) < 0
                        && Array.IndexOf(dependent.Conditions, condition) < 0)
                    {
                        return false;
                    }
                }

                if (updated.ConditionAlternatives.Length == 0)
                {
                    continue;
                }

                if (dependent.ConditionAlternatives.Length == 0)
                {
                    if (!HoldsAlternative(dependent.Conditions, [], updated.ConditionAlternatives))
                    {
                        return false;
                    }

                    continue;
                }

                foreach (var alternative in dependent.ConditionAlternatives)
                {
                    if (!HoldsAlternative(dependent.Conditions, alternative, updated.ConditionAlternatives))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Accepts the list when the plan build creates no more requests for any source schema than for the list
        // accepted before, and keeps its build. Returns false when the list is not accepted or the build limit is hit.
        public bool TryAccept(ImmutableList<PlanStep> before, ImmutableList<PlanStep> after)
        {
            if (_requestCheck.Accepted is null)
            {
                _requestCheck.Accepted = _requestCheck.Build(before, _forwardVariableContext);
                Count(_requestCheck.Accepted.Context, _acceptedCounts);
            }

            Debug.Assert(ReferenceEquals(_requestCheck.Accepted.Source, before));

            if (!_requestCheck.CanBuild)
            {
                return false;
            }

            var candidate = _requestCheck.Build(after, _forwardVariableContext);
            Count(candidate.Context, _candidateCounts);

            for (var i = 0; i < _schemaCount; i++)
            {
                if (_candidateCounts[i] > _acceptedCounts[i])
                {
                    return false;
                }
            }

            _requestCheck.Accepted = candidate;
            (_acceptedCounts, _candidateCounts) = (_candidateCounts, _acceptedCounts);
            return true;
        }

        // Counts the execution nodes that send a request to each source schema.
        private void Count(ExecutionPlanBuildContext ctx, int[] counts)
        {
            counts.AsSpan(0, _schemaCapacity).Clear();

            foreach (var node in ctx.ExecutionNodes.Values)
            {
                if (node is NodeFieldExecutionNode)
                {
                    continue;
                }

                counts[GetSchemaIndex(node.SchemaName ?? DynamicSchemaNameMarker)]++;
            }
        }

        private int GetSchemaIndex(string schemaName)
        {
            for (var i = 0; i < _schemaCount; i++)
            {
                if (schemaName.Equals(_schemaNames[i], StringComparison.Ordinal))
                {
                    return i;
                }
            }

            _schemaNames[_schemaCount] = schemaName;
            return _schemaCount++;
        }

        public void Dispose()
        {
            ArrayPool<string?>.Shared.Return(_schemaNames, clearArray: true);
            ArrayPool<int>.Shared.Return(_acceptedCounts);
            ArrayPool<int>.Shared.Return(_candidateCounts);
            ArrayPool<ConditionKey>.Shared.Return(_allowed, clearArray: true);
            ArrayPool<ISelectionNode>.Shared.Return(_kept, clearArray: true);
            ArrayPool<int>.Shared.Return(_holders);
            ArrayPool<OperationPlanStep?>.Shared.Return(_byId, clearArray: true);
            ArrayPool<int>.Shared.Return(_depth);
            ArrayPool<int>.Shared.Return(_remaining);
            ArrayPool<int>.Shared.Return(_queue);
            ArrayPool<int>.Shared.Return(_visited);
        }

        // Computes the dependency depth of every operation step, the longest chain of dependencies in front of it.
        private void Index(ImmutableList<PlanStep> planSteps, OperationPlanStep?[] byId, int[] depth)
        {
            byId.AsSpan(0, _maxId + 1).Clear();
            _remaining.AsSpan(0, _maxId + 1).Fill(-1);

            foreach (var planStep in planSteps)
            {
                if (planStep is OperationPlanStep step)
                {
                    byId[step.Id] = step;
                    _remaining[step.Id] = 0;
                    depth[step.Id] = 0;
                }
            }

            foreach (var planStep in planSteps)
            {
                if (planStep is not OperationPlanStep step)
                {
                    continue;
                }

                foreach (var dependentId in step.Dependents)
                {
                    if (dependentId > 0 && dependentId <= _maxId && byId[dependentId] is not null)
                    {
                        _remaining[dependentId]++;
                    }
                }
            }

            var head = 0;
            var tail = 0;

            foreach (var planStep in planSteps)
            {
                if (planStep is OperationPlanStep step && _remaining[step.Id] == 0)
                {
                    _queue[tail++] = step.Id;
                }
            }

            while (head < tail)
            {
                var current = byId[_queue[head++]]!;

                foreach (var dependentId in current.Dependents)
                {
                    if (dependentId <= 0 || dependentId > _maxId || byId[dependentId] is null)
                    {
                        continue;
                    }

                    depth[dependentId] = Math.Max(depth[dependentId], depth[current.Id] + 1);

                    if (--_remaining[dependentId] == 0)
                    {
                        _queue[tail++] = dependentId;
                    }
                }
            }
        }

        private bool IsEarlier(OperationPlanStep candidate, OperationPlanStep other)
        {
            var candidateIsPlain = candidate.DeliveryGroups.IsDefaultOrEmpty;

            if (candidateIsPlain != other.DeliveryGroups.IsDefaultOrEmpty)
            {
                return candidateIsPlain;
            }

            return _depth[candidate.Id] != _depth[other.Id]
                ? _depth[candidate.Id] < _depth[other.Id]
                : candidate.Id < other.Id;
        }

        // Checks that the holder has no delivery groups or every group of the step.
        private static bool HasEveryDeliveryGroup(ImmutableArray<int> stepGroups, ImmutableArray<int> holderGroups)
        {
            if (holderGroups.IsDefaultOrEmpty)
            {
                return true;
            }

            foreach (var stepGroup in stepGroups)
            {
                if (!holderGroups.Contains(stepGroup))
                {
                    return false;
                }
            }

            return true;
        }

        // Checks that every group of the step is a group of the holder or has an ancestor that is.
        private bool CoversEveryDeliveryGroup(ImmutableArray<int> stepGroups, ImmutableArray<int> holderGroups)
        {
            if (holderGroups.IsDefaultOrEmpty)
            {
                return true;
            }

            if (stepGroups.IsDefaultOrEmpty)
            {
                return false;
            }

            foreach (var stepGroup in stepGroups)
            {
                var covered = false;

                for (var group = _deliveryGroups[stepGroup]; group is not null; group = group.Parent)
                {
                    if (holderGroups.Contains(group.Id))
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    return false;
                }
            }

            return true;
        }

        private bool CoversDependents(OperationPlanStep step, OperationPlanStep holder)
        {
            foreach (var dependentId in step.Dependents)
            {
                if (dependentId <= _maxId
                    && _byId[dependentId] is { } dependent
                    && !CoversDeliveryGroups(dependent.DeliveryGroups, holder.DeliveryGroups, _deliveryGroups))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsReachable(int fromId, int toId)
        {
            var stamp = ++_visitStamp;
            var count = 0;
            _queue[count++] = fromId;
            _visited[fromId] = stamp;

            while (count > 0)
            {
                var current = _byId[_queue[--count]];

                if (current is null)
                {
                    continue;
                }

                foreach (var dependentId in current.Dependents)
                {
                    if (dependentId == toId)
                    {
                        return true;
                    }

                    if (dependentId > 0 && dependentId <= _maxId && _visited[dependentId] != stamp)
                    {
                        _visited[dependentId] = stamp;
                        _queue[count++] = dependentId;
                    }
                }
            }

            return false;
        }

        private bool AreConditionsAllowed(OperationPlanStep holder)
        {
            foreach (var condition in holder.Conditions)
            {
                if (!IsAllowed(new ConditionKey(condition.VariableName, condition.PassingValue)))
                {
                    return false;
                }
            }

            if (holder.ConditionAlternatives.Length == 0)
            {
                return true;
            }

            foreach (var alternative in holder.ConditionAlternatives)
            {
                var holds = true;

                foreach (var condition in alternative)
                {
                    if (!IsAllowed(new ConditionKey(condition.VariableName, condition.PassingValue)))
                    {
                        holds = false;
                        break;
                    }
                }

                if (holds)
                {
                    return true;
                }
            }

            return false;
        }

        // Navigates the path in the operation. A type condition of the path matches any fragment.
        private bool IsSelectedAt(
            SelectionSetNode current,
            SelectionPath path,
            int pathIndex,
            int group,
            FieldNode field)
        {
            if (pathIndex == path.Length)
            {
                return IsFieldSelected(current, group, field);
            }

            var segment = path[pathIndex];

            if (segment.Kind is SelectionPathSegmentKind.InlineFragment
                && IsSelectedAt(current, path, pathIndex + 1, group, field))
            {
                return true;
            }

            for (var i = 0; i < current.Selections.Count; i++)
            {
                switch (current.Selections[i])
                {
                    case FieldNode { SelectionSet: { } child } parentField
                        when segment.Kind is SelectionPathSegmentKind.Field
                            && (parentField.Alias?.Value ?? parentField.Name.Value)
                                .Equals(segment.Name, StringComparison.Ordinal)
                            && IsSelectedAt(child, path, pathIndex + 1, group, field):
                        return true;

                    case InlineFragmentNode fragment
                        when IsSelectedAt(fragment.SelectionSet, path, pathIndex, GetGroup(fragment, group), field):
                        return true;
                }
            }

            return false;
        }

        // Checks that the selection set selects a leaf of the field inside one of the groups of the step.
        private bool IsFieldSelected(SelectionSetNode current, int group, FieldNode field)
        {
            for (var i = 0; i < current.Selections.Count; i++)
            {
                switch (current.Selections[i])
                {
                    case FieldNode selected
                        when (selected.Alias?.Value ?? selected.Name.Value)
                            .Equals(field.Alias?.Value ?? field.Name.Value, StringComparison.Ordinal)
                            && (field.SelectionSet is null
                                ? _stepGroups.Contains(group)
                                : selected.SelectionSet is not null
                                    && AreLeavesSelected(selected.SelectionSet, group, field.SelectionSet)):
                        return true;

                    case InlineFragmentNode fragment
                        when IsFieldSelected(fragment.SelectionSet, GetGroup(fragment, group), field):
                        return true;
                }
            }

            return false;
        }

        private bool AreLeavesSelected(SelectionSetNode current, int group, SelectionSetNode required)
        {
            for (var i = 0; i < required.Selections.Count; i++)
            {
                switch (required.Selections[i])
                {
                    case FieldNode requiredField when IsFieldSelected(current, group, requiredField):
                        return true;

                    case InlineFragmentNode fragment when AreLeavesSelected(current, group, fragment.SelectionSet):
                        return true;
                }
            }

            return false;
        }

        // Returns the id of the delivery group that the fragment opens, or the enclosing group.
        private static int GetGroup(InlineFragmentNode fragment, int enclosingGroup)
        {
            for (var i = 0; i < fragment.Directives.Count; i++)
            {
                var directive = fragment.Directives[i];

                if (!directive.Name.Value.Equals("defer", StringComparison.Ordinal))
                {
                    continue;
                }

                for (var j = 0; j < directive.Arguments.Count; j++)
                {
                    if (directive.Arguments[j] is { Name.Value: "__group", Value: IntValueNode group })
                    {
                        return group.ToInt32();
                    }
                }
            }

            return enclosingGroup;
        }

        // Navigates the path below the holder, then looks for the field.
        private bool FetchesAtPath(SelectionSetNode current, SelectionPath path, int pathIndex, FieldNode required)
        {
            if (pathIndex == path.Length)
            {
                return ContainsField(current, required);
            }

            var segment = path[pathIndex];

            for (var i = 0; i < current.Selections.Count; i++)
            {
                switch (current.Selections[i])
                {
                    case InlineFragmentNode { TypeCondition: null } wrapper
                        when AreAllowed(wrapper.Directives)
                            && FetchesAtPath(wrapper.SelectionSet, path, pathIndex, required):
                        return true;

                    case FieldNode { SelectionSet: { } child } parentField
                        when segment.Kind is SelectionPathSegmentKind.Field
                            && (parentField.Alias?.Value ?? parentField.Name.Value)
                                .Equals(segment.Name, StringComparison.Ordinal)
                            && AreAllowed(parentField.Directives)
                            && FetchesAtPath(child, path, pathIndex + 1, required):
                        return true;

                    case InlineFragmentNode { TypeCondition: { } condition } fragment
                        when segment.Kind is SelectionPathSegmentKind.InlineFragment
                            && condition.Name.Value.Equals(segment.Name, StringComparison.Ordinal)
                            && AreAllowed(fragment.Directives)
                            && FetchesAtPath(fragment.SelectionSet, path, pathIndex + 1, required):
                        return true;
                }
            }

            return false;
        }

        // Looks for the field in the selection set, also inside inline fragments without type condition.
        private bool ContainsField(SelectionSetNode holder, FieldNode required)
        {
            for (var i = 0; i < holder.Selections.Count; i++)
            {
                switch (holder.Selections[i])
                {
                    case InlineFragmentNode { TypeCondition: null } wrapper
                        when AreAllowed(wrapper.Directives) && ContainsField(wrapper.SelectionSet, required):
                        return true;

                    case FieldNode field
                        when IsSameField(required, field)
                            && AreAllowed(field.Directives)
                            && (required.SelectionSet is null
                                || (field.SelectionSet is not null
                                    && ContainsSelections(field.SelectionSet, required.SelectionSet))):
                        return true;
                }
            }

            return false;
        }

        // Checks that the holder selection set fetches every selection of the required one under the allowed
        // conditions plus the conditions of the required selection.
        private bool ContainsSelections(SelectionSetNode holder, SelectionSetNode required)
        {
            for (var i = 0; i < required.Selections.Count; i++)
            {
                var restoreCount = _allowedCount;
                var found = required.Selections[i] switch
                {
                    FieldNode field => TryPush(field.Directives) && ContainsField(holder, field),
                    InlineFragmentNode { TypeCondition: null } wrapper
                        => TryPush(wrapper.Directives) && ContainsSelections(holder, wrapper.SelectionSet),
                    InlineFragmentNode fragment
                        => TryPush(fragment.Directives) && ContainsFragment(holder, fragment),
                    _ => false
                };

                _allowedCount = restoreCount;

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ContainsFragment(SelectionSetNode holder, InlineFragmentNode required)
        {
            for (var i = 0; i < holder.Selections.Count; i++)
            {
                switch (holder.Selections[i])
                {
                    case InlineFragmentNode { TypeCondition: null } wrapper
                        when AreAllowed(wrapper.Directives) && ContainsFragment(wrapper.SelectionSet, required):
                        return true;

                    case InlineFragmentNode { TypeCondition: { } condition } fragment
                        when condition.Name.Value.Equals(required.TypeCondition!.Name.Value, StringComparison.Ordinal)
                            && AreAllowed(fragment.Directives)
                            && ContainsSelections(fragment.SelectionSet, required.SelectionSet):
                        return true;
                }
            }

            return false;
        }

        private static bool IsSameField(FieldNode required, FieldNode field)
        {
            if (!required.Name.Value.Equals(field.Name.Value, StringComparison.Ordinal)
                || !string.Equals(required.Alias?.Value, field.Alias?.Value, StringComparison.Ordinal)
                || required.Arguments.Count != field.Arguments.Count)
            {
                return false;
            }

            for (var i = 0; i < required.Arguments.Count; i++)
            {
                if (!SyntaxComparer.BySyntax.Equals(required.Arguments[i], field.Arguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryPush(IReadOnlyList<DirectiveNode> directives)
        {
            for (var i = 0; i < directives.Count; i++)
            {
                if (!TryReadCondition(directives[i], out var condition) || _allowedCount == _allowed.Length)
                {
                    return false;
                }

                _allowed[_allowedCount++] = condition;
            }

            return true;
        }

        private bool AreAllowed(IReadOnlyList<DirectiveNode> directives)
        {
            for (var i = 0; i < directives.Count; i++)
            {
                if (!TryReadCondition(directives[i], out var condition) || !IsAllowed(condition))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsAllowed(ConditionKey condition)
        {
            for (var i = 0; i < _allowedCount; i++)
            {
                if (_allowed[i] == condition)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadCondition(DirectiveNode directive, out ConditionKey condition)
        {
            var passingValue = directive.Name.Value switch
            {
                "skip" => false,
                "include" => true,
                _ => (bool?)null
            };

            if (passingValue.HasValue
                && directive.Arguments is [{ Value: VariableNode variable }])
            {
                condition = new ConditionKey(variable.Name.Value, passingValue.Value);
                return true;
            }

            condition = default;
            return false;
        }
    }

    private static void IndexDependencies(
        ImmutableList<PlanStep> planSteps,
        ExecutionPlanBuildContext ctx)
    {
        foreach (var step in planSteps)
        {
            if (step is OperationPlanStep operationPlanStep)
            {
                // Plan steps store which steps they feed into ("dependents").
                // We invert that here so each step knows which steps it
                // depends on, which is what the executor needs for scheduling.
                foreach (var dependent in operationPlanStep.Dependents)
                {
                    if (!ctx.DependenciesByStepId.TryGetValue(dependent, out var dependencies))
                    {
                        dependencies = [];
                        ctx.DependenciesByStepId[dependent] = dependencies;
                    }

                    dependencies.Add(step.Id);
                }
            }
            else if (step is NodeFieldPlanStep nodePlanStep)
            {
                foreach (var (_, dependent) in nodePlanStep.Branches)
                {
                    if (!ctx.DependenciesByStepId.TryGetValue(dependent.Id, out var dependencies))
                    {
                        dependencies = [];
                        ctx.DependenciesByStepId[dependent.Id] = dependencies;
                    }

                    dependencies.Add(nodePlanStep.Id);

                    // In source-schema resolution the branch enriches the entity produced by the
                    // fallback query, so it must run after the fallback query has resolved the
                    // concrete type into the node result.
                    if (nodePlanStep.SourceSchemaResolution)
                    {
                        dependencies.Add(nodePlanStep.FallbackQuery.Id);
                    }
                }

                if (!ctx.DependenciesByStepId.TryGetValue(nodePlanStep.FallbackQuery.Id, out var fallbackDependencies))
                {
                    fallbackDependencies = [];
                    ctx.DependenciesByStepId[nodePlanStep.FallbackQuery.Id] = fallbackDependencies;
                }

                fallbackDependencies.Add(nodePlanStep.Id);

                ctx.BranchesByNodeId.Add(
                    nodePlanStep.Id,
                    nodePlanStep.Branches.ToDictionary(x => x.Key, x => x.Value.Id));
                ctx.FallbackByNodeId.Add(nodePlanStep.Id, nodePlanStep.FallbackQuery.Id);
            }
        }
    }

    private static void BuildExecutionNodes(
        ImmutableList<PlanStep> planSteps,
        ExecutionPlanBuildContext ctx,
        FusionSchemaDefinition schema,
        bool hasVariables,
        CancellationToken cancellationToken)
    {
        var requiresUpload = schema.Types.TryGetType(UploadScalarName, out var uploadType) && uploadType.IsScalarType();
        var readySteps = planSteps.Where(t => !ctx.DependenciesByStepId.ContainsKey(t.Id)).ToList();
        var variableBuffer = hasVariables ? new List<string>() : null;

        while (ctx.ProcessedStepIds.Count < planSteps.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var step in readySteps)
            {
                if (!ctx.ProcessedStepIds.Add(step.Id))
                {
                    continue;
                }

                if (step is OperationPlanStep operationStep)
                {
                    ctx.ExecutionNodes.Add(
                        step.Id,
                        operationStep.EventStreamPlan is null
                            ? CreateOperationExecutionNode(
                                operationStep,
                                ctx,
                                schema,
                                requiresUpload,
                                variableBuffer)
                            : CreateEventStreamExecutionNode(operationStep, schema));
                }
                else if (step is NodeFieldPlanStep nodeStep)
                {
                    ctx.ExecutionNodes.Add(step.Id,
                        new NodeFieldExecutionNode(nodeStep.Id, nodeStep.ResponseName, nodeStep.IdValue, nodeStep.Conditions));
                }
            }

            readySteps.Clear();

            foreach (var step in planSteps)
            {
                if (ctx.DependenciesByStepId.TryGetValue(step.Id, out var stepDependencies)
                    && ctx.ProcessedStepIds.IsSupersetOf(stepDependencies))
                {
                    readySteps.Add(step);
                }
            }

            if (readySteps.Count == 0)
            {
                break;
            }
        }

        // Every plan step must be schedulable. If any remain unprocessed the plan has a cyclic
        // step dependency, which is an internal planner invariant violation rather than a
        // user-facing condition. We fail loudly instead of silently emitting a degenerate plan.
        if (ctx.ProcessedStepIds.Count < planSteps.Count)
        {
            var unschedulableStepIds = new List<int>();

            foreach (var step in planSteps)
            {
                if (!ctx.ProcessedStepIds.Contains(step.Id))
                {
                    unschedulableStepIds.Add(step.Id);
                }
            }

            throw new InvalidOperationException(
                "The execution plan could not be built because the following plan steps have a "
                + $"cyclic dependency and cannot be scheduled: {string.Join(", ", unschedulableStepIds)}.");
        }
    }

    private static ExecutionNode CreateOperationExecutionNode(
        OperationPlanStep operationStep,
        ExecutionPlanBuildContext ctx,
        FusionSchemaDefinition schema,
        bool requiresUpload,
        List<string>? variableBuffer)
    {
        var requirements = operationStep.Requirements.IsEmpty
            ? []
            : operationStep.Requirements.OrderBy(t => t.Key).Select(t => t.Value).ToArray();

        var forwardedVariables = Array.Empty<string>();

        if (variableBuffer is not null && operationStep.Definition.VariableDefinitions.Count > 0)
        {
            variableBuffer.Clear();
            var requirementKeys = new HashSet<string>(requirements.Select(r => r.Key));

            foreach (var variableDef in operationStep.Definition.VariableDefinitions)
            {
                var name = variableDef.Variable.Name.Value;

                if (!requirementKeys.Contains(name))
                {
                    variableBuffer.Add(name);
                }
            }

            if (variableBuffer.Count > 0)
            {
                forwardedVariables = variableBuffer.ToArray();
            }
        }

        var requiresFileUpload = requiresUpload
            && DoVariablesContainUploadScalar(operationStep.Definition.VariableDefinitions, schema);

        var sourceRewrite = operationStep.SchemaName is null
            ? SourceFieldTypeMismatchRewriter.RewriteDynamic(
                operationStep.Definition,
                schema.GetOperationType(operationStep.Definition.Operation),
                schema)
            : SourceFieldTypeMismatchRewriter.Rewrite(
                operationStep.Definition,
                schema.GetOperationType(operationStep.Definition.Operation),
                operationStep.SchemaName,
                schema);
        var operation = RemoveInternalDirectives(sourceRewrite.Operation);
        var operationSource = operation.ToSourceText();

        // A lookup operation reads its data from the single root selection that the planner
        // emitted for the lookup, so the step's type is the type its body is selected on.
        var lookupTypeName = operationStep.Lookup is null ? null : operationStep.Type.Name;

        var selectionSetNode = GetSelectionSetNodeFromPath(operationStep.Definition, operationStep.Source);
        var sourceAliasSelectionSets = CreateSourceAliasSelectionSetLookup(
            selectionSetNode,
            sourceRewrite.Aliases);
        selectionSetNode = PruneNonValueTypeChildren(
            selectionSetNode,
            operationStep.Type,
            schema,
            operationStep.SchemaName,
            sourceRewrite.Aliases,
            sourceAliasSelectionSets);
        var resultSelectionSet = ResultSelectionSet.Create(
            selectionSetNode,
            schema,
            operationStep.Type,
            operationStep.SchemaName,
            sourceRewrite.Aliases);

        // Only synthetic internal key lookups resolve through _entities. Real public
        // root-field lookups, for example the composed node field, stay native even
        // on Apollo schemas.
        if (operationStep.Lookup is { IsInternal: true } lookup
            && schema.GetSourceSchemaConnectorKind(operationStep.SchemaName ?? lookup.SchemaName)
                == ConnectorKindNames.ApolloFederation)
        {
            if (operationStep.SchemaName is null)
            {
                throw new InvalidOperationException(
                    $"The lookup '{lookup.FieldName}' targets the Apollo Federation source schema "
                    + $"'{lookup.SchemaName}', but the plan step does not specify a concrete source "
                    + "schema name. Apollo Federation lookups cannot be resolved dynamically.");
            }

            var apolloNode = ApolloOperationExecutionNode.CreateFromLookup(
                operationStep.Id,
                operationSource,
                operationStep.SchemaName,
                operationStep.Target,
                requirements,
                forwardedVariables,
                resultSelectionSet,
                operationStep.Conditions,
                operationStep.ConditionAlternatives,
                requiresFileUpload,
                schema);

            foreach (var parentDependency in operationStep.ParentDependencies)
            {
                apolloNode.AddParentDependency(parentDependency.StepId);
            }

            ctx.ApolloLookupOperationsByStepId.Add(operationStep.Id, operationSource);
            return apolloNode;
        }

        var node = new OperationExecutionNode(
            operationStep.Id,
            operationSource,
            lookupTypeName,
            operationStep.SchemaName,
            operationStep.Target,
            operationStep.Source,
            requirements,
            forwardedVariables,
            resultSelectionSet,
            operationStep.Conditions,
            operationStep.ConditionAlternatives,
            requiresFileUpload)
        {
            DeliveryGroups = operationStep.DeliveryGroups
        };

        foreach (var parentDependency in operationStep.ParentDependencies)
        {
            node.AddParentDependency(parentDependency.StepId);
        }

        return node;
    }

    private static EventStreamExecutionNode CreateEventStreamExecutionNode(
        OperationPlanStep operationStep,
        FusionSchemaDefinition schema)
    {
        var eventStreamPlan = operationStep.EventStreamPlan
            ?? throw new InvalidOperationException("The operation step does not carry event-stream metadata.");

        var selectionSetNode = GetSelectionSetNodeFromPath(operationStep.Definition, operationStep.Source);
        selectionSetNode = PruneNonValueTypeChildren(
            selectionSetNode,
            operationStep.Type,
            schema,
            operationStep.SchemaName,
            sourceAliases: null,
            sourceAliasSelectionSets: null);
        var resultSelectionSet = ResultSelectionSet.Create(
            selectionSetNode,
            schema,
            operationStep.Type,
            operationStep.SchemaName);

        var node = new EventStreamExecutionNode(
            operationStep.Id,
            eventStreamPlan.FieldName,
            operationStep.Target,
            operationStep.Source,
            resultSelectionSet,
            eventStreamPlan.Source,
            eventStreamPlan.Message,
            operationStep.Conditions);

        foreach (var parentDependency in operationStep.ParentDependencies)
        {
            node.AddParentDependency(parentDependency.StepId);
        }

        return node;
    }

    /// <summary>
    /// Maps every execution node of a mutation plan to the serial phase it runs in, which is the
    /// position of the last root node it transitively depends on.
    /// </summary>
    private static Dictionary<int, int> CreateSerialScopes(
        ImmutableList<PlanStep> planSteps,
        ExecutionPlanBuildContext ctx)
    {
        var scopes = new Dictionary<int, int>(ctx.ExecutionNodes.Count);
        var rootPosition = 0;

        foreach (var step in planSteps)
        {
            if (ctx.ExecutionNodes.ContainsKey(step.Id) && !ctx.DependenciesByStepId.ContainsKey(step.Id))
            {
                scopes[step.Id] = rootPosition++;
            }
        }

        // A node runs after all of its dependencies, so visiting nodes by dependency depth
        // resolves the scope of every dependency before the scope of its dependents.
        var depthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();

        foreach (var nodeId in ctx.ExecutionNodes.Keys)
        {
            GetDependencyDepth(nodeId, ctx.DependenciesByStepId, depthLookup, recursionStack);
        }

        foreach (var (nodeId, _) in depthLookup.OrderBy(t => t.Value))
        {
            if (scopes.ContainsKey(nodeId))
            {
                continue;
            }

            var scope = 0;

            if (ctx.DependenciesByStepId.TryGetValue(nodeId, out var dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    if (scopes.TryGetValue(dependency, out var dependencyScope))
                    {
                        scope = Math.Max(scope, dependencyScope);
                    }
                }
            }

            scopes[nodeId] = scope;
        }

        return scopes;
    }

    private static int GetSerialScope(Dictionary<int, int>? serialScopes, int nodeId)
        => serialScopes is not null && serialScopes.TryGetValue(nodeId, out var scope) ? scope : 0;

    /// <summary>
    /// Replaces query operations that fetch the same selections for the same target under different
    /// conditions by one operation that runs when the conditions of at least one of them pass.
    /// </summary>
    private static void MergeOperationsThatDifferInConditions(
        ExecutionPlanBuildContext ctx,
        OperationMergePolicy mergePolicy,
        Dictionary<int, int>? serialScopes)
    {
        if (mergePolicy is OperationMergePolicy.None)
        {
            return;
        }

        // The pass repeats until no operations merge.
        while (TryMergeOperationsThatDifferInConditions(ctx, mergePolicy, serialScopes))
        {
        }
    }

    private static bool TryMergeOperationsThatDifferInConditions(
        ExecutionPlanBuildContext ctx,
        OperationMergePolicy mergePolicy,
        Dictionary<int, int>? serialScopes)
    {
        var merged = false;
        var nodeFieldBoundCache = new Dictionary<int, bool>();
        var candidates = new Dictionary<string, List<OperationExecutionNode>>(StringComparer.Ordinal);

        foreach (var node in ctx.ExecutionNodes.Values.OfType<OperationExecutionNode>())
        {
            if (node.Operation.Type != OperationType.Query
                || IsNodeFieldBound(node.Id, ctx, nodeFieldBoundCache))
            {
                continue;
            }

            var signature = ComputeCanonicalSignature(
                node.SchemaName,
                node.Source,
                node.OperationDocument,
                node.Requirements,
                [],
                [],
                node.DeliveryGroups);
            var key = $"{GetSerialScope(serialScopes, node.Id)}|{node.Target}|{signature}";

            if (!candidates.TryGetValue(key, out var list))
            {
                list = [];
                candidates[key] = list;
            }

            list.Add(node);
        }

        foreach (var (_, equivalentNodes) in candidates)
        {
            if (equivalentNodes.Count <= 1)
            {
                continue;
            }

            foreach (var mergeableNodes in PartitionIntoMergeableGroups(
                equivalentNodes, ctx.DependenciesByStepId, mergePolicy))
            {
                foreach (var group in PartitionByDependencies(mergeableNodes, ctx.DependenciesByStepId))
                {
                    if (group.Count > 1 && !HaveEqualConditions(group))
                    {
                        MergeIntoOperationWithConditionAlternatives(ctx, group);
                        merged = true;
                    }
                }
            }
        }

        return merged;
    }

    // Splits the nodes into groups with equal dependency sets. A node without an entry has an empty set.
    private static List<List<OperationExecutionNode>> PartitionByDependencies(
        List<OperationExecutionNode> nodes,
        Dictionary<int, HashSet<int>> dependenciesByStepId)
    {
        var groups = new List<List<OperationExecutionNode>>();

        foreach (var node in nodes)
        {
            dependenciesByStepId.TryGetValue(node.Id, out var dependencies);
            var placed = false;

            foreach (var group in groups)
            {
                dependenciesByStepId.TryGetValue(group[0].Id, out var groupDependencies);

                if (HaveEqualDependencies(dependencies, groupDependencies))
                {
                    group.Add(node);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                groups.Add([node]);
            }
        }

        return groups;
    }

    private static bool HaveEqualDependencies(HashSet<int>? left, HashSet<int>? right)
    {
        if (left is null || left.Count == 0)
        {
            return right is null || right.Count == 0;
        }

        return right is not null && left.SetEquals(right);
    }

    private static bool HaveEqualConditions(List<OperationExecutionNode> group)
    {
        var signature = ComputeCanonicalSignature(group[0]);

        for (var i = 1; i < group.Count; i++)
        {
            if (!ComputeCanonicalSignature(group[i]).Equals(signature, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // Each member contributes one term per condition alternative, or one term when it has none. The
    // merged operation passes when one term passes, which is exactly when one member would run.
    private static void MergeIntoOperationWithConditionAlternatives(
        ExecutionPlanBuildContext ctx,
        List<OperationExecutionNode> group)
    {
        group.Sort((a, b) => a.Id.CompareTo(b.Id));

        var terms = new List<HashSet<ExecutionNodeCondition>>();

        foreach (var member in group)
        {
            if (member.ConditionAlternatives.Length == 0)
            {
                terms.Add([.. member.Conditions]);
                continue;
            }

            foreach (var alternative in member.ConditionAlternatives)
            {
                terms.Add([.. member.Conditions, .. alternative]);
            }
        }

        var common = new HashSet<ExecutionNodeCondition>(terms[0]);

        foreach (var term in terms)
        {
            common.IntersectWith(term);
        }

        var primary = group[0];
        var merged = new OperationExecutionNode(
            primary.Id,
            primary.Operation,
            primary.LookupTypeName,
            primary.SchemaName,
            primary.Target,
            primary.Source,
            primary.Requirements.ToArray(),
            primary.ForwardedVariables.ToArray(),
            primary.ResultSelectionSet,
            SortConditions(common),
            BuildConditionAlternatives(terms, common),
            primary.RequiresFileUpload)
        {
            DeliveryGroups = primary.DeliveryGroups
        };

        foreach (var member in group)
        {
            foreach (var parentDependency in member.BufferedParentDependencies)
            {
                merged.AddParentDependency(parentDependency);
            }
        }

        ctx.ExecutionNodes[primary.Id] = merged;
        group[0] = merged;
        AbsorbMergedNodes(ctx, primary.Id, group);
    }

    private static void MergeAndBatchOperations(
        ExecutionPlanBuildContext ctx,
        bool enableRequestGrouping,
        OperationMergePolicy mergePolicy,
        bool splitForEntityReuse,
        Dictionary<int, int>? serialScopes,
        FusionSchemaDefinition schema)
    {
        var nodeFieldBoundCache = new Dictionary<int, bool>();
        var mergeResults = MergeStructurallyIdenticalOperations(
            ctx,
            nodeFieldBoundCache,
            mergePolicy,
            splitForEntityReuse,
            serialScopes,
            schema);
        var apolloMergeResults = MergeStructurallyIdenticalApolloLookups(
            ctx,
            nodeFieldBoundCache,
            mergePolicy,
            splitForEntityReuse,
            serialScopes,
            schema);

        // Capture each node's dependency identifiers now, because the batching
        // step below will rewrite the dependency lookup as it merges nodes.
        var originalDependencies = new Dictionary<int, int[]>(ctx.DependenciesByStepId.Count);

        foreach (var (nodeId, dependencies) in ctx.DependenciesByStepId)
        {
            originalDependencies[nodeId] = dependencies.ToArray();
        }

        var perOperationDependencies = GroupBySchemaAndDepthIntoBatches(
            ctx, nodeFieldBoundCache, mergeResults, originalDependencies, enableRequestGrouping, serialScopes);

        foreach (var (batchNode, memberDependencies) in GroupApolloLookupsIntoBatches(
            ctx,
            nodeFieldBoundCache,
            apolloMergeResults,
            originalDependencies,
            enableRequestGrouping,
            serialScopes,
            schema))
        {
            perOperationDependencies.Add(batchNode, memberDependencies);
        }

        WrapRemainingMergedOperations(ctx, mergeResults, perOperationDependencies, originalDependencies);
        WirePerOperationDependencies(ctx, perOperationDependencies);
    }

    /// <summary>
    /// Finds query operations that are structurally identical and merges
    /// them into a single <see cref="BatchOperationDefinition"/>. This
    /// reduces the number of network requests the executor has to send.
    /// When <paramref name="splitForEntityReuse"/> is set, identical lookups at
    /// different dependency depths stay apart and each deeper lookup depends on
    /// the next shallower one, so that entity reuse can serve it.
    /// Operations in different <paramref name="serialScopes"/> are never merged.
    /// </summary>
    private static Dictionary<int, MergeResult> MergeStructurallyIdenticalOperations(
        ExecutionPlanBuildContext ctx,
        Dictionary<int, bool> nodeFieldBoundCache,
        OperationMergePolicy mergePolicy,
        bool splitForEntityReuse,
        Dictionary<int, int>? serialScopes,
        FusionSchemaDefinition schema)
    {
        if (mergePolicy is OperationMergePolicy.None)
        {
            return [];
        }

        var candidates = new Dictionary<string, List<OperationExecutionNode>>(StringComparer.Ordinal);
        var queryNodes = ctx.ExecutionNodes.Values.OfType<OperationExecutionNode>();

        if (!ctx.DeliveryGroups.IsDefaultOrEmpty)
        {
            queryNodes = OrderByDependencyDepthDescending(ctx, queryNodes);
        }

        foreach (var node in queryNodes)
        {
            if (node.Operation.Type != OperationType.Query)
            {
                continue;
            }

            if (IsNodeFieldBound(node.Id, ctx, nodeFieldBoundCache))
            {
                continue;
            }

            var signature = serialScopes is null
                ? ComputeMergeSignature(ctx, node)
                : $"{GetSerialScope(serialScopes, node.Id)}|{ComputeMergeSignature(ctx, node)}";

            if (!candidates.TryGetValue(signature, out var list))
            {
                list = [];
                candidates[signature] = list;
            }

            list.Add(node);
        }

        var mergeResults = new Dictionary<int, MergeResult>();

        foreach (var (_, equivalentNodes) in candidates)
        {
            if (equivalentNodes.Count <= 1)
            {
                continue;
            }

            if (splitForEntityReuse && IsReusableLookup(equivalentNodes[0], schema))
            {
                List<int> shallowerIds = [];

                foreach (var level in GroupByDependencyDepth(equivalentNodes, ctx.DependenciesByStepId))
                {
                    List<int> levelIds = [];

                    foreach (var group in PartitionIntoMergeableGroups(
                        level, ctx.DependenciesByStepId, mergePolicy))
                    {
                        levelIds.Add(MergeGroup(ctx, group, mergeResults));
                    }

                    foreach (var levelId in levelIds)
                    {
                        foreach (var shallowerId in shallowerIds)
                        {
                            AddDependency(ctx, levelId, shallowerId);
                        }
                    }

                    shallowerIds = levelIds;
                }

                continue;
            }

            foreach (var group in PartitionIntoMergeableGroups(
                equivalentNodes, ctx.DependenciesByStepId, mergePolicy))
            {
                MergeGroup(ctx, group, mergeResults);
            }
        }

        return mergeResults;
    }

    // Orders the nodes from the deepest dependency level to the shallowest.
    private static IEnumerable<OperationExecutionNode> OrderByDependencyDepthDescending(
        ExecutionPlanBuildContext ctx,
        IEnumerable<OperationExecutionNode> nodes)
    {
        var depthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();
        var ordered = nodes.ToList();

        foreach (var node in ordered)
        {
            GetDependencyDepth(node.Id, ctx.DependenciesByStepId, depthLookup, recursionStack);
        }

        return ordered.OrderByDescending(node => depthLookup[node.Id]);
    }

    private static ImmutableArray<int> GetEffectiveDeliveryGroups(
        OperationExecutionNode node,
        Dictionary<int, MergeResult> mergeResults)
        => mergeResults.TryGetValue(node.Id, out var merge) ? merge.DeliveryGroups : node.DeliveryGroups;

    /// <summary>
    /// Merges Apollo Federation entity lookups that are structurally identical and share
    /// a source schema into one lookup with a target per merged node, subject to the merge policy.
    /// Cross-depth merges require the shallower lookup to have no dependents and the deeper
    /// lookup to already wait for all of its dependencies. Lookups in different
    /// <paramref name="serialScopes"/> are never merged. When <paramref name="splitForEntityReuse"/>
    /// is set, reusable lookups at different dependency depths stay apart and each deeper
    /// lookup runs after the next shallower one, so that entity reuse can serve it.
    /// </summary>
    private static Dictionary<int, ApolloOperationExecutionNode[]> MergeStructurallyIdenticalApolloLookups(
        ExecutionPlanBuildContext ctx,
        Dictionary<int, bool> nodeFieldBoundCache,
        OperationMergePolicy mergePolicy,
        bool splitForEntityReuse,
        Dictionary<int, int>? serialScopes,
        FusionSchemaDefinition schema)
    {
        if (mergePolicy is OperationMergePolicy.None)
        {
            return [];
        }

        var mergeResults = new Dictionary<int, ApolloOperationExecutionNode[]>();

        var lookupNodes = ctx.ExecutionNodes.Values
            .OfType<ApolloOperationExecutionNode>()
            .Where(n => n.Operation.Type == OperationType.Query)
            .Where(n => !IsNodeFieldBound(n.Id, ctx, nodeFieldBoundCache))
            .ToList();

        if (lookupNodes.Count < 2)
        {
            return mergeResults;
        }

        var depthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();

        foreach (var node in lookupNodes)
        {
            GetDependencyDepth(node.Id, ctx.DependenciesByStepId, depthLookup, recursionStack);
        }

        var groupsBySignature = new Dictionary<string, List<ApolloLookupGroup>>(StringComparer.Ordinal);

        foreach (var node in lookupNodes)
        {
            var depth = depthLookup.TryGetValue(node.Id, out var d) ? d : 0;
            var signature = serialScopes is null
                ? ComputeApolloCanonicalSignature(ctx, node)
                : $"{GetSerialScope(serialScopes, node.Id)}|{ComputeApolloCanonicalSignature(ctx, node)}";

            if (!groupsBySignature.TryGetValue(signature, out var groups))
            {
                groups = [];
                groupsBySignature[signature] = groups;
            }

            var group = groups.Find(g => g.Depth == depth);

            if (group is null)
            {
                group = new ApolloLookupGroup(depth);
                groups.Add(group);
            }

            group.Nodes.Add(node);
        }

        foreach (var groups in groupsBySignature.Values)
        {
            foreach (var group in groups)
            {
                if (group.Nodes.Count > 1)
                {
                    group.Nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
                    AbsorbMergedNodes(ctx, group.Nodes[0].Id, group.Nodes);
                }
            }
        }

        // Folding runs after all same-depth merges, so the dependency graph
        // it inspects already references the merged lookups.
        foreach (var groups in groupsBySignature.Values)
        {
            if (groups.Count > 1
                && splitForEntityReuse
                && IsReusableApolloLookup(groups[0].Nodes[0], schema))
            {
                groups.Sort((a, b) => a.Depth.CompareTo(b.Depth));

                for (var i = 1; i < groups.Count; i++)
                {
                    AddDependency(ctx, groups[i].Nodes[0].Id, groups[i - 1].Nodes[0].Id);
                }
            }
            else if (groups.Count > 1 && mergePolicy is not OperationMergePolicy.Conservative)
            {
                FoldLeafApolloLookupGroups(ctx, groups, mergePolicy);
            }

            foreach (var group in groups)
            {
                if (group.Nodes.Count > 1)
                {
                    mergeResults[group.Nodes[0].Id] = [.. group.Nodes];
                }
            }
        }

        return mergeResults;
    }

    /// <summary>
    /// Folds every group in <paramref name="groups"/> that no node depends on into the nearest
    /// deeper group whose transitive dependencies contain all direct dependencies of the folded
    /// group. Folded groups are removed from <paramref name="groups"/>.
    /// </summary>
    private static void FoldLeafApolloLookupGroups(
        ExecutionPlanBuildContext ctx,
        List<ApolloLookupGroup> groups,
        OperationMergePolicy mergePolicy)
    {
        groups.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        var visited = new HashSet<int>();
        var i = 0;

        while (i < groups.Count - 1)
        {
            var leaf = groups[i];
            var leafPrimary = leaf.Nodes[0];
            ApolloLookupGroup? target = null;

            if (!HasDependents(ctx.DependenciesByStepId, leafPrimary.Id))
            {
                for (var j = i + 1; j < groups.Count; j++)
                {
                    if (mergePolicy is OperationMergePolicy.Balanced
                        && groups[j].Depth - leaf.MinDepth > 1)
                    {
                        break;
                    }

                    if (AreTransitiveDependencies(
                        ctx.DependenciesByStepId,
                        leafPrimary.Id,
                        groups[j].Nodes[0].Id,
                        visited))
                    {
                        target = groups[j];
                        break;
                    }
                }
            }

            if (target is null)
            {
                i++;
                continue;
            }

            var targetPrimary = target.Nodes[0];

            if (leafPrimary.Id < targetPrimary.Id)
            {
                AbsorbMergedNodes(ctx, leafPrimary.Id, [leafPrimary, targetPrimary]);
            }
            else
            {
                AbsorbMergedNodes(ctx, targetPrimary.Id, [targetPrimary, leafPrimary]);
            }

            target.MinDepth = Math.Min(target.MinDepth, leaf.MinDepth);
            target.Nodes.AddRange(leaf.Nodes);
            target.Nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
            groups.RemoveAt(i);
        }
    }

    private static bool HasDependents(Dictionary<int, HashSet<int>> dependenciesByStepId, int nodeId)
    {
        foreach (var dependencies in dependenciesByStepId.Values)
        {
            if (dependencies.Contains(nodeId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether every direct dependency of <paramref name="nodeId"/> is also a
    /// transitive dependency of <paramref name="otherNodeId"/>.
    /// </summary>
    private static bool AreTransitiveDependencies(
        Dictionary<int, HashSet<int>> dependenciesByStepId,
        int nodeId,
        int otherNodeId,
        HashSet<int> visited)
    {
        if (!dependenciesByStepId.TryGetValue(nodeId, out var dependencies))
        {
            return true;
        }

        foreach (var dependency in dependencies)
        {
            visited.Clear();

            if (!IsTransitivelyReachable(otherNodeId, dependency, dependenciesByStepId, visited))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Merges a group of structurally identical operations into its primary node
    /// and returns the id of the node that represents the group.
    /// </summary>
    private static int MergeGroup(
        ExecutionPlanBuildContext ctx,
        List<OperationExecutionNode> group,
        Dictionary<int, MergeResult> mergeResults)
    {
        if (group.Count <= 1)
        {
            return group[0].Id;
        }

        group.Sort((a, b) => a.Id.CompareTo(b.Id));

        var primary = group[0];
        var (canonicalOp, canonicalRequirements) = CanonicalizeOperation(primary);
        var deliveryGroups = UnionDeliveryGroups(group, mergeResults);

        Debug.Assert(
            deliveryGroups.Length < 2 || AreUnlabelledSiblingsWithEqualConditions(ctx, group, deliveryGroups),
            "A step of several delivery groups must merge unlabelled sibling groups with equal conditions.");

        var targets = new SelectionPath[group.Count];

        for (var i = 0; i < group.Count; i++)
        {
            targets[i] = group[i].Target;
        }

        // Merged operations of sibling groups keep each target once.
        if (deliveryGroups != primary.DeliveryGroups)
        {
            targets = [.. targets.Distinct()];
        }

        mergeResults[primary.Id] = new MergeResult(
            targets, canonicalOp, canonicalRequirements, primary, deliveryGroups);

        AbsorbMergedNodes(ctx, primary.Id, group);

        return primary.Id;
    }

    // Checks that the groups are unlabelled siblings and that the members hold equal conditions.
    private static bool AreUnlabelledSiblingsWithEqualConditions(
        ExecutionPlanBuildContext ctx,
        List<OperationExecutionNode> group,
        ImmutableArray<int> deliveryGroups)
    {
        if (!TryGetUnlabelledSite(ctx.DeliveryGroups, deliveryGroups, out _))
        {
            return false;
        }

        var conditions = FormatCanonicalConditions(group[0].Conditions);
        var alternatives = FormatCanonicalConditionAlternatives(group[0].ConditionAlternatives);

        for (var i = 1; i < group.Count; i++)
        {
            if (FormatCanonicalConditions(group[i].Conditions) != conditions
                || FormatCanonicalConditionAlternatives(group[i].ConditionAlternatives) != alternatives)
            {
                return false;
            }
        }

        return true;
    }

    // Returns the groups of all members, sorted by group id.
    private static ImmutableArray<int> UnionDeliveryGroups(
        List<OperationExecutionNode> group,
        Dictionary<int, MergeResult> mergeResults)
    {
        var primaryGroups = GetEffectiveDeliveryGroups(group[0], mergeResults);
        SortedSet<int>? union = null;

        for (var i = 1; i < group.Count; i++)
        {
            var memberGroups = GetEffectiveDeliveryGroups(group[i], mergeResults);

            if (memberGroups.IsDefaultOrEmpty || memberGroups.AsSpan().SequenceEqual(primaryGroups.AsSpan()))
            {
                continue;
            }

            union ??= [.. primaryGroups];
            union.UnionWith(memberGroups);
        }

        return union is null ? primaryGroups : [.. union];
    }

    private static bool IsReusableLookup(OperationExecutionNode node, FusionSchemaDefinition schema)
        => IsReusableLookup(
            node.LookupTypeName,
            node.ResultSelectionSet,
            node.Requirements,
            node.ForwardedVariables.Length,
            node.Conditions.Length + node.ConditionAlternatives.Length,
            schema);

    private static bool IsReusableApolloLookup(ApolloOperationExecutionNode node, FusionSchemaDefinition schema)
        => IsReusableLookup(
                node.Lookup.EntityTypeName,
                node.ResultSelectionSet,
                node.Requirements,
                node.ForwardedVariables.Length,
                node.Conditions.Length + node.ConditionAlternatives.Length,
                schema)
            && EntityReuseCandidateMap.TryGetReusableFields(
                node.ResultSelectionSet,
                node.Requirements,
                node.ForwardedVariables.Length,
                node.Conditions.Length + node.ConditionAlternatives.Length,
                out _,
                out var requirementFields)
            && EntityReuseCandidateMap.IsFlatRepresentationShape(node.Lookup.RepresentationShape, requirementFields);

    private static bool IsReusableLookup(
        string? lookupTypeName,
        ResultSelectionSet resultSelectionSet,
        ReadOnlySpan<OperationRequirement> requirements,
        int forwardedVariableCount,
        int conditionCount,
        FusionSchemaDefinition schema)
    {
        if (lookupTypeName is null
            || !schema.Types.TryGetType<FusionComplexTypeDefinition>(lookupTypeName, out var lookupType)
            || !EntityReuseCandidateMap.TryGetReusableFields(
                resultSelectionSet,
                requirements,
                forwardedVariableCount,
                conditionCount,
                out var responseNames,
                out _))
        {
            return false;
        }

        // Entity reuse only takes leaf values.
        foreach (var responseName in responseNames)
        {
            if (!lookupType.Fields.TryGetField(responseName, out var field)
                || !field.Type.NamedType().IsLeafType())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Groups nodes by their dependency depth, ordered from the shallowest to the deepest level.
    /// </summary>
    private static List<List<OperationExecutionNode>> GroupByDependencyDepth(
        List<OperationExecutionNode> nodes,
        Dictionary<int, HashSet<int>> dependenciesByStepId)
    {
        var depthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();
        var levels = new SortedDictionary<int, List<OperationExecutionNode>>();

        foreach (var node in nodes)
        {
            var depth = GetDependencyDepth(node.Id, dependenciesByStepId, depthLookup, recursionStack);

            if (!levels.TryGetValue(depth, out var level))
            {
                level = [];
                levels[depth] = level;
            }

            level.Add(node);
        }

        return [.. levels.Values];
    }

    private static void AddDependency(ExecutionPlanBuildContext ctx, int nodeId, int dependencyId)
    {
        if (!ctx.DependenciesByStepId.TryGetValue(nodeId, out var dependencies))
        {
            dependencies = [];
            ctx.DependenciesByStepId[nodeId] = dependencies;
        }

        dependencies.Add(dependencyId);
    }

    /// <summary>
    /// Removes merged nodes from the execution graph and folds their
    /// dependencies into the primary node that represents them all.
    /// </summary>
    private static void AbsorbMergedNodes(
        ExecutionPlanBuildContext ctx,
        int primaryId,
        IReadOnlyList<ExecutionNode> group)
    {
        var absorbedIds = new HashSet<int>(group.Count - 1);

        if (!ctx.DependenciesByStepId.TryGetValue(primaryId, out var primaryDeps))
        {
            primaryDeps = [];
        }

        for (var i = 1; i < group.Count; i++)
        {
            var otherId = group[i].Id;
            absorbedIds.Add(otherId);
            ctx.ExecutionNodes.Remove(otherId);
            ctx.RedirectedStepIds[otherId] = primaryId;

            if (ctx.DependenciesByStepId.TryGetValue(otherId, out var otherDependencies))
            {
                foreach (var dependency in otherDependencies)
                {
                    primaryDeps.Add(dependency);
                }

                ctx.DependenciesByStepId.Remove(otherId);
            }
        }

        if (primaryDeps.Count > 0)
        {
            ctx.DependenciesByStepId[primaryId] = primaryDeps;
        }
        else
        {
            ctx.DependenciesByStepId.Remove(primaryId);
        }

        RedirectDependencyReferences(ctx.DependenciesByStepId, absorbedIds, primaryId);
    }

    /// <summary>
    /// Groups query nodes by their target schema and dependency depth into
    /// batch execution nodes. Nodes at the same depth targeting the same
    /// source schema are independent of each other, so the executor can
    /// send them together in a single batched network request.
    /// Nodes in different <paramref name="serialScopes"/> are never batched.
    /// </summary>
    private static Dictionary<ExecutionNode, Dictionary<int, int[]>> GroupBySchemaAndDepthIntoBatches(
        ExecutionPlanBuildContext ctx,
        Dictionary<int, bool> nodeFieldBoundCache,
        Dictionary<int, MergeResult> mergeResults,
        Dictionary<int, int[]> originalDependencies,
        bool enableRequestGrouping,
        Dictionary<int, int>? serialScopes)
    {
        var consumedMergeIds = new HashSet<int>();
        var perOperationDependencies = new Dictionary<ExecutionNode, Dictionary<int, int[]>>();

        if (!enableRequestGrouping)
        {
            return perOperationDependencies;
        }

        var queryNodes = ctx.ExecutionNodes.Values
            .OfType<OperationExecutionNode>()
            .Where(n => n.Operation.Type == OperationType.Query)
            .Where(n => !IsNodeFieldBound(n.Id, ctx, nodeFieldBoundCache))
            .ToList();

        var depthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();

        foreach (var node in queryNodes)
        {
            GetDependencyDepth(node.Id, ctx.DependenciesByStepId, depthLookup, recursionStack);
        }

        var batchGroups =
            new Dictionary<
                (string schema, int depth, int scope, string deliveryGroups),
                List<OperationExecutionNode>>();

        foreach (var node in queryNodes)
        {
            var schemaKey = node.SchemaName ?? DynamicSchemaNameMarker;
            var depth = depthLookup.TryGetValue(node.Id, out var d) ? d : 0;
            var key = (
                schemaKey,
                depth,
                GetSerialScope(serialScopes, node.Id),
                FormatBatchMark(ctx, node.DeliveryGroups));

            if (!batchGroups.TryGetValue(key, out var group))
            {
                group = [];
                batchGroups[key] = group;
            }

            group.Add(node);
        }

        foreach (var group in batchGroups.Values)
        {
            group.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        // Process from shallowest to deepest so that deeper groups reference
        // the already-redirected identifiers from earlier merges. Groups at
        // the same depth are ordered by their lowest member identifier so
        // batch node identifier assignment is deterministic.
        foreach (var (_, groupMembers) in batchGroups
            .OrderBy(t => t.Key.depth)
            .ThenBy(t => t.Value[0].Id))
        {
            if (groupMembers.Count <= 1)
            {
                continue;
            }

            var operations = new List<OperationDefinition>();

            foreach (var member in groupMembers)
            {
                if (mergeResults.TryGetValue(member.Id, out var merge))
                {
                    consumedMergeIds.Add(member.Id);
                    operations.Add(CreateBatchOperationDefinition(merge));
                }
                else
                {
                    operations.Add(CreateSingleOperationDefinition(member));
                }
            }

            var batchNodeId = ctx.NextId();
            var batchNode = new OperationBatchExecutionNode(batchNodeId, operations.ToArray());

            // Save each member's dependencies before replacing the individual
            // nodes, because the replacement will remove them from the lookup.
            var memberDependencies = new Dictionary<int, int[]>();

            foreach (var member in groupMembers)
            {
                if (originalDependencies.TryGetValue(member.Id, out var memberDeps))
                {
                    memberDependencies[member.Id] = memberDeps;
                }
            }

            ReplaceMembersWithBatchNode(ctx, groupMembers, batchNode, batchNodeId);
            perOperationDependencies[batchNode] = memberDependencies;
        }

        Debug.Assert(
            AreDependenciesOnBatchedGroupStepsCovered(ctx, originalDependencies),
            "A dependency on a step of a batch must name a member of one batch whose conditions the dependent holds.");

        // Remove consumed merge results so the caller knows which ones still
        // need to be wrapped as standalone batch nodes.
        foreach (var id in consumedMergeIds)
        {
            mergeResults.Remove(id);
        }

        return perOperationDependencies;
    }

    // Checks that every dependency on a batched step of a delivery group names a member of one batch
    // and that the dependent holds the conditions of that member.
    private static bool AreDependenciesOnBatchedGroupStepsCovered(
        ExecutionPlanBuildContext ctx,
        Dictionary<int, int[]> originalDependencies)
    {
        if (ctx.DeliveryGroups.IsDefaultOrEmpty)
        {
            return true;
        }

        var members = new Dictionary<int, OperationDefinition>();

        foreach (var node in ctx.ExecutionNodes.Values)
        {
            if (node is not OperationBatchExecutionNode batch)
            {
                continue;
            }

            foreach (var operation in batch.Operations)
            {
                if (!members.TryAdd(operation.Id, operation))
                {
                    return false;
                }
            }
        }

        foreach (var (dependentId, dependencyIds) in originalDependencies)
        {
            foreach (var dependencyId in dependencyIds)
            {
                if (!members.TryGetValue(dependencyId, out var member) || member.DeliveryGroups.IsDefaultOrEmpty)
                {
                    continue;
                }

                ExecutionNodeCondition[] conditions;
                ExecutionNodeCondition[][] alternatives;

                if (members.TryGetValue(dependentId, out var dependentMember))
                {
                    conditions = dependentMember.Conditions.ToArray();
                    alternatives = dependentMember.ConditionAlternatives.ToArray();
                }
                else if (ctx.ExecutionNodes.TryGetValue(dependentId, out var step)
                    && step is OperationExecutionNode dependent)
                {
                    conditions = dependent.Conditions.ToArray();
                    alternatives = dependent.ConditionAlternatives.ToArray();
                }
                else
                {
                    continue;
                }

                foreach (var condition in member.Conditions)
                {
                    if (Array.IndexOf(conditions, condition) < 0)
                    {
                        return false;
                    }
                }

                if (member.ConditionAlternatives.Length == 0)
                {
                    continue;
                }

                if (alternatives.Length == 0)
                {
                    if (!HoldsAlternative(conditions, [], member.ConditionAlternatives))
                    {
                        return false;
                    }

                    continue;
                }

                foreach (var alternative in alternatives)
                {
                    if (!HoldsAlternative(conditions, alternative, member.ConditionAlternatives))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    // Checks that the two condition sets together contain every condition of one of the alternatives.
    private static bool HoldsAlternative(
        ExecutionNodeCondition[] conditions,
        ExecutionNodeCondition[] alternative,
        ReadOnlySpan<ExecutionNodeCondition[]> alternatives)
    {
        foreach (var candidate in alternatives)
        {
            var holds = true;

            foreach (var condition in candidate)
            {
                if (Array.IndexOf(conditions, condition) < 0 && Array.IndexOf(alternative, condition) < 0)
                {
                    holds = false;
                    break;
                }
            }

            if (holds)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Groups Apollo Federation entity lookups by their target schema and
    /// dependency depth into batch execution nodes. Lookups at the same depth
    /// targeting the same source schema are independent of each other, so the
    /// executor can send them together in a single batched network request.
    /// Lookups in different <paramref name="serialScopes"/> are never batched.
    /// </summary>
    private static Dictionary<ExecutionNode, Dictionary<int, int[]>> GroupApolloLookupsIntoBatches(
        ExecutionPlanBuildContext ctx,
        Dictionary<int, bool> nodeFieldBoundCache,
        Dictionary<int, ApolloOperationExecutionNode[]> mergeResults,
        Dictionary<int, int[]> originalDependencies,
        bool enableRequestGrouping,
        Dictionary<int, int>? serialScopes,
        FusionSchemaDefinition schema)
    {
        var perOperationDependencies = new Dictionary<ExecutionNode, Dictionary<int, int[]>>();

        var lookupNodes = ctx.ExecutionNodes.Values
            .OfType<ApolloOperationExecutionNode>()
            .Where(n => n.Operation.Type == OperationType.Query)
            .Where(n => !IsNodeFieldBound(n.Id, ctx, nodeFieldBoundCache))
            .Where(n => enableRequestGrouping || mergeResults.ContainsKey(n.Id))
            .ToList();

        var depthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();

        foreach (var node in lookupNodes)
        {
            GetDependencyDepth(node.Id, ctx.DependenciesByStepId, depthLookup, recursionStack);
        }

        var batchGroups =
            new Dictionary<(string schema, int depth, int scope, int operationId), List<ApolloOperationExecutionNode>>();

        foreach (var node in lookupNodes)
        {
            // Apollo lookup nodes always carry a concrete schema name because
            // the routing in CreateOperationExecutionNode rejects dynamic ones.
            var depth = depthLookup.TryGetValue(node.Id, out var d) ? d : 0;
            var key = (
                node.SchemaName,
                depth,
                GetSerialScope(serialScopes, node.Id),
                enableRequestGrouping ? 0 : node.Id);

            if (!batchGroups.TryGetValue(key, out var group))
            {
                group = [];
                batchGroups[key] = group;
            }

            group.Add(node);
        }

        foreach (var group in batchGroups.Values)
        {
            group.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        // Process from shallowest to deepest so that deeper groups reference
        // the already-redirected identifiers from earlier merges. Groups at
        // the same depth are ordered by their lowest member identifier so
        // batch node identifier assignment is deterministic.
        foreach (var (_, groupMembers) in batchGroups
            .OrderBy(t => t.Key.depth)
            .ThenBy(t => t.Value[0].Id))
        {
            // A merged lookup runs through the batch node even when it is alone,
            // because only the batch node executes multi-target lookups.
            if (groupMembers.Count <= 1 && !mergeResults.ContainsKey(groupMembers[0].Id))
            {
                continue;
            }

            var operations = new OperationDefinition[groupMembers.Count];

            for (var i = 0; i < groupMembers.Count; i++)
            {
                var member = groupMembers[i];
                operations[i] = mergeResults.TryGetValue(member.Id, out var mergedNodes)
                    ? CreateApolloBatchOperationDefinition(ctx, mergedNodes)
                    : CreateApolloSingleOperationDefinition(ctx, member);
            }

            var batchNodeId = ctx.NextId();
            var batchNode = ApolloOperationBatchExecutionNode.CreateFromLookup(batchNodeId, operations, schema);

            // Save each member's dependencies before replacing the individual
            // nodes, because the replacement will remove them from the lookup.
            var memberDependencies = new Dictionary<int, int[]>();

            foreach (var member in groupMembers)
            {
                if (originalDependencies.TryGetValue(member.Id, out var memberDeps))
                {
                    memberDependencies[member.Id] = memberDeps;
                }
            }

            ReplaceMembersWithBatchNode(ctx, groupMembers, batchNode, batchNodeId);
            perOperationDependencies[batchNode] = memberDependencies;
        }

        return perOperationDependencies;
    }

    /// <summary>
    /// Wraps merged operations that were not included in any multi-member
    /// batch group into standalone batch execution nodes.
    /// </summary>
    private static void WrapRemainingMergedOperations(
        ExecutionPlanBuildContext ctx,
        Dictionary<int, MergeResult> remainingMerges,
        Dictionary<ExecutionNode, Dictionary<int, int[]>> perOperationDependencies,
        Dictionary<int, int[]> originalDependencies)
    {
        foreach (var (primaryId, merge) in remainingMerges.OrderBy(t => t.Key))
        {
            var operationDefinition = CreateBatchOperationDefinition(merge);
            var batchNodeId = ctx.NextId();
            var standaloneBatchNode = new OperationBatchExecutionNode(batchNodeId, [operationDefinition]);
            ReplaceMembersWithBatchNode(ctx, [merge.Primary], standaloneBatchNode, batchNodeId);

            perOperationDependencies[standaloneBatchNode] =
                new Dictionary<int, int[]>
                {
                    [operationDefinition.Id] = originalDependencies.TryGetValue(primaryId, out var primaryDeps)
                        ? primaryDeps
                        : []
                };
        }
    }

    /// <summary>
    /// Connects each inner operation inside a batch node to the upstream
    /// operation definitions it depends on. This gives the executor
    /// fine-grained visibility into per-operation readiness.
    /// </summary>
    private static void WirePerOperationDependencies(
        ExecutionPlanBuildContext ctx,
        Dictionary<ExecutionNode, Dictionary<int, int[]>> perOperationDependencies)
    {
        if (perOperationDependencies.Count == 0)
        {
            return;
        }

        var planNodeById = new Dictionary<int, IOperationPlanNode>();

        foreach (var node in ctx.ExecutionNodes.Values)
        {
            planNodeById[node.Id] = node;

            if (node is OperationBatchExecutionNode batch)
            {
                foreach (var operation in batch.Operations)
                {
                    planNodeById[operation.Id] = operation;
                }
            }

            if (node is ApolloOperationBatchExecutionNode apolloBatch)
            {
                foreach (var operation in apolloBatch.Operations)
                {
                    planNodeById[operation.Id] = operation;
                }
            }
        }

        var apolloDefinitions = IndexApolloDefinitions(ctx.ExecutionNodes.Values);

        foreach (var (batchNode, memberDependencies) in perOperationDependencies)
        {
            foreach (var (operationId, dependencyIds) in memberDependencies)
            {
                if (planNodeById.TryGetValue(operationId, out var operationNode)
                    && operationNode is OperationDefinition operationDefinition)
                {
                    foreach (var dependencyId in dependencyIds)
                    {
                        if (planNodeById.TryGetValue(dependencyId, out var dependencyNode))
                        {
                            if ((batchNode is OperationBatchExecutionNode
                                    && IsEntityReuseOrderingDependency(operationDefinition, dependencyNode))
                                || (batchNode is ApolloOperationBatchExecutionNode apolloBatch
                                    && apolloDefinitions.TryGetValue(operationDefinition.Id, out var location)
                                    && ReferenceEquals(location.Batch, apolloBatch)
                                    && IsEntityReuseOrderingDependency(
                                        apolloBatch,
                                        location.Index,
                                        dependencyNode,
                                        apolloDefinitions)))
                            {
                                operationDefinition.AddOptionalDependency(dependencyNode);
                            }
                            else
                            {
                                operationDefinition.AddDependency(dependencyNode);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Replaces individual member nodes in the execution graph with a single
    /// batch node, merging all of their dependencies into the batch node.
    /// </summary>
    private static void ReplaceMembersWithBatchNode(
        ExecutionPlanBuildContext ctx,
        IReadOnlyList<ExecutionNode> members,
        ExecutionNode batchNode,
        int batchNodeId)
    {
        var batchDependencies = new HashSet<int>();
        var memberIds = new HashSet<int>(members.Count);

        foreach (var member in members)
        {
            memberIds.Add(member.Id);
            ctx.ExecutionNodes.Remove(member.Id);
            ctx.RedirectedStepIds[member.Id] = batchNodeId;

            if (ctx.DependenciesByStepId.TryGetValue(member.Id, out var memberDependencies))
            {
                foreach (var dependency in memberDependencies)
                {
                    batchDependencies.Add(dependency);
                }

                ctx.DependenciesByStepId.Remove(member.Id);
            }
        }

        ctx.ExecutionNodes[batchNodeId] = batchNode;

        if (batchDependencies.Count > 0)
        {
            ctx.DependenciesByStepId[batchNodeId] = batchDependencies;
        }

        RedirectDependencyReferences(ctx.DependenciesByStepId, memberIds, batchNodeId);
    }

    private static int ResolveRedirectedStepId(int id, Dictionary<int, int> redirectedStepIds)
    {
        var current = id;

        for (var hops = 0; hops < redirectedStepIds.Count; hops++)
        {
            if (!redirectedStepIds.TryGetValue(current, out var next))
            {
                break;
            }

            current = next;
        }

        return current;
    }

    private static BatchOperationDefinition CreateBatchOperationDefinition(MergeResult merge)
    {
        var primary = merge.Primary;

        var definition = new BatchOperationDefinition(
            primary.Id,
            merge.CanonicalOp,
            primary.LookupTypeName,
            primary.SchemaName,
            merge.Targets,
            primary.Source,
            merge.CanonicalRequirements,
            primary.ForwardedVariables.ToArray(),
            primary.ResultSelectionSet,
            primary.Conditions.ToArray(),
            primary.ConditionAlternatives.ToArray(),
            primary.RequiresFileUpload)
        {
            DeliveryGroups = merge.DeliveryGroups
        };

        foreach (var parentDependency in primary.BufferedParentDependencies)
        {
            definition.AddParentDependency(parentDependency);
        }

        return definition;
    }

    private static SingleOperationDefinition CreateSingleOperationDefinition(OperationExecutionNode member)
    {
        var definition = new SingleOperationDefinition(
            member.Id,
            member.Operation,
            member.LookupTypeName,
            member.SchemaName,
            member.Target,
            member.Source,
            member.Requirements.ToArray(),
            member.ForwardedVariables.ToArray(),
            member.ResultSelectionSet,
            member.Conditions.ToArray(),
            member.ConditionAlternatives.ToArray(),
            member.RequiresFileUpload)
        {
            DeliveryGroups = member.DeliveryGroups
        };

        foreach (var parentDependency in member.BufferedParentDependencies)
        {
            definition.AddParentDependency(parentDependency);
        }

        return definition;
    }

    private static SingleOperationDefinition CreateApolloSingleOperationDefinition(
        ExecutionPlanBuildContext ctx,
        ApolloOperationExecutionNode member)
    {
        if (!ctx.ApolloLookupOperationsByStepId.TryGetValue(member.Id, out var lookupOperation))
        {
            throw ThrowHelper.ApolloLookupOperationMissing(member.Id);
        }

        var definition = new SingleOperationDefinition(
            member.Id,
            lookupOperation,
            lookupTypeName: null,
            member.SchemaName,
            member.Target,
            member.Source,
            member.Requirements.ToArray(),
            member.ForwardedVariables.ToArray(),
            member.ResultSelectionSet,
            member.Conditions.ToArray(),
            member.ConditionAlternatives.ToArray(),
            member.RequiresFileUpload);

        foreach (var parentDependency in member.BufferedParentDependencies)
        {
            definition.AddParentDependency(parentDependency);
        }

        return definition;
    }

    private static BatchOperationDefinition CreateApolloBatchOperationDefinition(
        ExecutionPlanBuildContext ctx,
        ApolloOperationExecutionNode[] mergedNodes)
    {
        var primary = mergedNodes[0];

        if (!ctx.ApolloLookupOperationsByStepId.TryGetValue(primary.Id, out var lookupOperation))
        {
            throw ThrowHelper.ApolloLookupOperationMissing(primary.Id);
        }

        var targets = new SelectionPath[mergedNodes.Length];

        for (var i = 0; i < mergedNodes.Length; i++)
        {
            targets[i] = mergedNodes[i].Target;
        }

        var definition = new BatchOperationDefinition(
            primary.Id,
            lookupOperation,
            lookupTypeName: null,
            primary.SchemaName,
            targets,
            primary.Source,
            primary.Requirements.ToArray(),
            primary.ForwardedVariables.ToArray(),
            primary.ResultSelectionSet,
            primary.Conditions.ToArray(),
            primary.ConditionAlternatives.ToArray(),
            primary.RequiresFileUpload);

        var parentDependencies = new HashSet<int>();

        foreach (var mergedNode in mergedNodes)
        {
            foreach (var parentDependency in mergedNode.BufferedParentDependencies)
            {
                if (parentDependencies.Add(parentDependency))
                {
                    definition.AddParentDependency(parentDependency);
                }
            }
        }

        return definition;
    }

    /// <summary>
    /// Rewrites the dependency graph so that every reference to any of
    /// <paramref name="oldIds"/> points to <paramref name="newId"/> instead.
    /// </summary>
    private static void RedirectDependencyReferences(
        Dictionary<int, HashSet<int>> dependenciesByStepId,
        HashSet<int> oldIds,
        int newId)
    {
        foreach (var depSet in dependenciesByStepId.Values)
        {
            var hadOld = false;

            foreach (var oldId in oldIds)
            {
                if (depSet.Remove(oldId))
                {
                    hadOld = true;
                }
            }

            if (hadOld)
            {
                depSet.Add(newId);
            }
        }
    }

    /// <summary>
    /// Checks whether a node is transitively dependent on a
    /// <see cref="NodeFieldExecutionNode"/>. Operations beneath a node-field
    /// dispatch must keep their original identifiers because the dispatch
    /// logic references them by identifier to select the correct branch.
    /// </summary>
    private static bool IsNodeFieldBound(
        int nodeId,
        ExecutionPlanBuildContext ctx,
        Dictionary<int, bool> cache)
    {
        if (cache.TryGetValue(nodeId, out var cached))
        {
            return cached;
        }

        if (!ctx.DependenciesByStepId.TryGetValue(nodeId, out var dependencies) || dependencies.Count == 0)
        {
            cache[nodeId] = false;
            return false;
        }

        foreach (var dependencyId in dependencies)
        {
            if (ctx.ExecutionNodes.TryGetValue(dependencyId, out var dependencyNode)
                && dependencyNode is NodeFieldExecutionNode)
            {
                cache[nodeId] = true;
                return true;
            }

            if (IsNodeFieldBound(dependencyId, ctx, cache))
            {
                cache[nodeId] = true;
                return true;
            }
        }

        cache[nodeId] = false;
        return false;
    }

    private static void WireExecutionDependencies(ExecutionPlanBuildContext ctx)
    {
        WireOperationDependencies(ctx);
        WireNodeFieldBranchesAndFallbacks(ctx);
    }

    private static void WireOperationDependencies(ExecutionPlanBuildContext ctx)
    {
        // Build a lookup from every operation identifier to its containing
        // execution node. A batch node wraps several operations, so each
        // inner operation identifier also maps back to the parent batch node.
        var executionNodeById = new Dictionary<int, ExecutionNode>();

        foreach (var node in ctx.ExecutionNodes.Values)
        {
            executionNodeById[node.Id] = node;

            if (node is OperationBatchExecutionNode batch)
            {
                foreach (var operation in batch.Operations)
                {
                    executionNodeById[operation.Id] = batch;
                }
            }

            if (node is ApolloOperationBatchExecutionNode apolloBatch)
            {
                foreach (var operation in apolloBatch.Operations)
                {
                    executionNodeById[operation.Id] = apolloBatch;
                }
            }
        }

        foreach (var (nodeId, stepDependencies) in ctx.DependenciesByStepId)
        {
            if (!ctx.ExecutionNodes.TryGetValue(nodeId, out var entry)
                || entry is not (
                    OperationExecutionNode
                    or OperationBatchExecutionNode
                    or ApolloOperationExecutionNode
                    or ApolloOperationBatchExecutionNode))
            {
                continue;
            }

            if (entry is OperationBatchExecutionNode batchEntry)
            {
                WireBatchNodeDependencies(
                    batchEntry,
                    batchEntry.Operations.Length > 1 || batchEntry.Operations[0] is BatchOperationDefinition,
                    stepDependencies,
                    executionNodeById);
                continue;
            }

            if (entry is ApolloOperationBatchExecutionNode apolloBatchEntry)
            {
                WireBatchNodeDependencies(
                    apolloBatchEntry,
                    apolloBatchEntry.Operations.Length > 1
                        || apolloBatchEntry.Operations[0] is BatchOperationDefinition,
                    stepDependencies,
                    executionNodeById);
                continue;
            }

            // For a standalone execution node, attach dependencies directly.
            foreach (var dependencyId in stepDependencies)
            {
                if (!ctx.ExecutionNodes.TryGetValue(dependencyId, out var childEntry)
                    || childEntry is not (
                        OperationExecutionNode
                        or OperationBatchExecutionNode
                        or ApolloOperationExecutionNode
                        or ApolloOperationBatchExecutionNode
                        or NodeFieldExecutionNode
                        or EventStreamExecutionNode))
                {
                    continue;
                }

                childEntry.AddDependent(entry);

                if (IsEntityReuseOrderingDependency(entry, childEntry))
                {
                    entry.AddOptionalDependency(childEntry);
                }
                else
                {
                    entry.AddDependency(childEntry);
                }
            }
        }
    }

    private static void WireBatchNodeDependencies(
        ExecutionNode batchEntry,
        bool useOptionalDependencies,
        HashSet<int> stepDependencies,
        Dictionary<int, ExecutionNode> executionNodeById)
    {
        var seenExecutionDependencies = new HashSet<int>();

        foreach (var dependencyId in stepDependencies)
        {
            if (dependencyId == batchEntry.Id)
            {
                continue;
            }

            if (!executionNodeById.TryGetValue(dependencyId, out var dependencyExecutionNode)
                || dependencyExecutionNode.Id == batchEntry.Id)
            {
                continue;
            }

            if (!seenExecutionDependencies.Add(dependencyExecutionNode.Id))
            {
                continue;
            }

            dependencyExecutionNode.AddDependent(batchEntry);

            // Operations inside a batch track their own dependencies, so batch
            // nodes with multiple operations or a single merged multi-target
            // operation take optional dependencies. Single-target operation
            // nodes require strict dependencies.
            if (useOptionalDependencies)
            {
                batchEntry.AddOptionalDependency(dependencyExecutionNode);
            }
            else
            {
                batchEntry.AddDependency(dependencyExecutionNode);
            }
        }
    }

    private static void WireNodeFieldBranchesAndFallbacks(ExecutionPlanBuildContext ctx)
    {
        foreach (var (nodeId, branches) in ctx.BranchesByNodeId)
        {
            if (!ctx.ExecutionNodes.TryGetValue(nodeId, out var entry) || entry is not NodeFieldExecutionNode node)
            {
                continue;
            }

            foreach (var (typeName, branchNodeId) in branches)
            {
                if (ctx.ExecutionNodes.TryGetValue(branchNodeId, out var branchNode))
                {
                    node.AddBranch(typeName, branchNode);
                }
            }
        }

        foreach (var (nodeId, fallbackNodeId) in ctx.FallbackByNodeId)
        {
            if (!ctx.ExecutionNodes.TryGetValue(nodeId, out var entry) || entry is not NodeFieldExecutionNode node)
            {
                continue;
            }

            if (ctx.ExecutionNodes.TryGetValue(fallbackNodeId, out var fallbackNode))
            {
                node.AddFallbackQuery(fallbackNode);
            }
        }
    }

    internal static Dictionary<int, int> CreateBatchingGroupLookup(
        ImmutableList<PlanStep> planSteps,
        Dictionary<int, HashSet<int>> dependencyLookup,
        bool enableRequestGrouping)
    {
        if (!enableRequestGrouping)
        {
            return [];
        }

        var queryStepsByService = new Dictionary<string, List<OperationPlanStep>>(StringComparer.Ordinal);

        foreach (var operationStep in planSteps.OfType<OperationPlanStep>())
        {
            if (operationStep.Definition.Operation is not OperationType.Query)
            {
                continue;
            }

            var schemaKey = operationStep.SchemaName ?? DynamicSchemaNameMarker;

            if (!queryStepsByService.TryGetValue(schemaKey, out var serviceSteps))
            {
                serviceSteps = [];
                queryStepsByService[schemaKey] = serviceSteps;
            }

            serviceSteps.Add(operationStep);
        }

        if (queryStepsByService.Count == 0)
        {
            return [];
        }

        var dependencyDepthLookup = new Dictionary<int, int>();
        var recursionStack = new HashSet<int>();

        foreach (var serviceSteps in queryStepsByService.Values)
        {
            foreach (var step in serviceSteps)
            {
                GetDependencyDepth(
                    step.Id,
                    dependencyLookup,
                    dependencyDepthLookup,
                    recursionStack);
            }
        }

        var lookup = new Dictionary<int, int>();
        var nextGroupId = 0;

        foreach (var (_, serviceSteps) in queryStepsByService.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var stepsByDepth = new Dictionary<int, List<int>>();

            foreach (var step in serviceSteps)
            {
                var depth = dependencyDepthLookup.TryGetValue(step.Id, out var currentDepth)
                    ? currentDepth
                    : 0;

                if (!stepsByDepth.TryGetValue(depth, out var groupedSteps))
                {
                    groupedSteps = [];
                    stepsByDepth.Add(depth, groupedSteps);
                }

                groupedSteps.Add(step.Id);
            }

            foreach (var groupedSteps in stepsByDepth.OrderBy(t => t.Key).Select(t => t.Value))
            {
                if (groupedSteps.Count <= 1)
                {
                    continue;
                }

                groupedSteps.Sort();
                var groupId = ++nextGroupId;

                foreach (var stepId in groupedSteps)
                {
                    lookup.Add(stepId, groupId);
                }
            }
        }

        return lookup;
    }

    private static int GetDependencyDepth(
        int stepId,
        Dictionary<int, HashSet<int>> dependenciesByStepId,
        Dictionary<int, int> depthLookup,
        HashSet<int> recursionStack)
    {
        if (depthLookup.TryGetValue(stepId, out var depth))
        {
            return depth;
        }

        if (!dependenciesByStepId.TryGetValue(stepId, out var directDependencies) || directDependencies.Count == 0)
        {
            depthLookup[stepId] = 0;
            return 0;
        }

        if (!recursionStack.Add(stepId))
        {
            throw new InvalidOperationException("The execution dependency graph contains a cycle.");
        }

        var maxDepth = 0;

        foreach (var dependency in directDependencies.OrderBy(t => t))
        {
            var dependencyDepth = GetDependencyDepth(
                dependency,
                dependenciesByStepId,
                depthLookup,
                recursionStack);
            maxDepth = Math.Max(maxDepth, dependencyDepth + 1);
        }

        recursionStack.Remove(stepId);
        depthLookup[stepId] = maxDepth;
        return maxDepth;
    }

    private static string ComputeCanonicalSignature(OperationExecutionNode node)
        => ComputeCanonicalSignature(
            node.SchemaName,
            node.Source,
            node.OperationDocument,
            node.Requirements,
            node.Conditions,
            node.ConditionAlternatives,
            node.DeliveryGroups);

    private static string ComputeCanonicalSignature(
        string? schemaName,
        SelectionPath source,
        Utf8OperationDocument document,
        ReadOnlySpan<OperationRequirement> requirements,
        ReadOnlySpan<ExecutionNodeCondition> conditions,
        ReadOnlySpan<ExecutionNodeCondition[]> conditionAlternatives,
        ImmutableArray<int> deliveryGroups)
        => ComputeCanonicalSignature(
            schemaName,
            source,
            document,
            requirements,
            conditions,
            conditionAlternatives,
            FormatDeliveryGroups(deliveryGroups));

    // Computes the signature with the marks of unlabelled sibling groups unified.
    private static string ComputeMergeSignature(ExecutionPlanBuildContext ctx, OperationExecutionNode node)
        => ComputeCanonicalSignature(
            node.SchemaName,
            node.Source,
            node.OperationDocument,
            node.Requirements,
            node.Conditions,
            node.ConditionAlternatives,
            FormatMergeMark(ctx, node.DeliveryGroups));

    private static string ComputeCanonicalSignature(
        string? schemaName,
        SelectionPath source,
        Utf8OperationDocument document,
        ReadOnlySpan<OperationRequirement> requirements,
        ReadOnlySpan<ExecutionNodeCondition> conditions,
        ReadOnlySpan<ExecutionNodeCondition[]> conditionAlternatives,
        string deliveryGroupMark)
    {
        var replacements = BuildPrefixReplacements(requirements);
        var bodyText = FormatCanonicalSelectionSet(document, replacements);
        var formattedConditions = FormatCanonicalConditions(conditions);
        var formattedAlternatives = FormatCanonicalConditionAlternatives(conditionAlternatives);
        var signature = $"{schemaName}|{source}|{formattedConditions}|{formattedAlternatives}|{bodyText}";

        return deliveryGroupMark.Length == 0
            ? signature
            : $"{signature}|{deliveryGroupMark}";
    }

    private static string FormatDeliveryGroups(ImmutableArray<int> deliveryGroups)
        => deliveryGroups.IsDefaultOrEmpty
            ? string.Empty
            : string.Join(",", deliveryGroups.OrderBy(static id => id));

    // Names the groups of a mark. Unlabelled groups with the same parent and path share one name.
    private static string FormatMergeMark(ExecutionPlanBuildContext ctx, ImmutableArray<int> deliveryGroups)
        => TryGetUnlabelledSite(ctx.DeliveryGroups, deliveryGroups, out var site)
            ? $"unlabelled:{site.Parent?.Id}:{site.Path}"
            : FormatDeliveryGroups(deliveryGroups);

    // Names the groups of a batch key. Every unlabelled group shares one name, whatever its parent and path.
    private static string FormatBatchMark(ExecutionPlanBuildContext ctx, ImmutableArray<int> deliveryGroups)
        => TryGetUnlabelledSite(ctx.DeliveryGroups, deliveryGroups, out _)
            ? UnlabelledBatchMark
            : FormatDeliveryGroups(deliveryGroups);

    // Checks that the groups are unlabelled and share one parent and path, and returns the first of them.
    private static bool TryGetUnlabelledSite(
        ImmutableArray<DeliveryGroup> allGroups,
        ImmutableArray<int> deliveryGroups,
        [NotNullWhen(true)] out DeliveryGroup? site)
    {
        site = null;

        if (deliveryGroups.IsDefaultOrEmpty || allGroups.IsDefaultOrEmpty)
        {
            return false;
        }

        var first = allGroups[deliveryGroups[0]];

        foreach (var id in deliveryGroups)
        {
            var group = allGroups[id];

            if (group.Label is not null
                || group.Path is null
                || group.Parent?.Id != first.Parent?.Id
                || !group.Path.Equals(first.Path))
            {
                return false;
            }
        }

        site = first;
        return true;
    }

    /// <summary>
    /// Determines whether <paramref name="dependent"/> runs after <paramref name="dependency"/>
    /// only so that entity reuse can serve it. That is the case when the dependency is, or
    /// contains, the same lookup that only selects leaf fields, so it cannot supply any data
    /// the dependent needs.
    /// </summary>
    internal static bool IsEntityReuseOrderingDependency(
        IOperationPlanNode dependent,
        IOperationPlanNode dependency)
    {
        if (dependency is OperationBatchExecutionNode batch)
        {
            foreach (var operation in batch.Operations)
            {
                if (IsEntityReuseOrderingDependency(dependent, operation))
                {
                    return true;
                }
            }

            return false;
        }

        if (dependent is ApolloOperationExecutionNode apolloDependent)
        {
            return IsApolloEntityReuseOrderingDependency(
                ApolloLookupSignatureSource.From(apolloDependent),
                dependency);
        }

        if (dependency is ApolloOperationExecutionNode or ApolloOperationBatchExecutionNode)
        {
            return false;
        }

        return TryGetLookupKey(dependent, out var schemaName, out var lookupTypeName)
            && TryGetLookupKey(dependency, out var dependencySchemaName, out var dependencyLookupTypeName)
            && string.Equals(schemaName, dependencySchemaName, StringComparison.Ordinal)
            && string.Equals(lookupTypeName, dependencyLookupTypeName, StringComparison.Ordinal)
            && SelectsOnlyReusableFields(dependent)
            && ComputeLookupSignature(dependent).Equals(ComputeLookupSignature(dependency), StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the operation at <paramref name="index"/> of an Apollo batch runs after
    /// <paramref name="dependency"/> only so that entity reuse can serve it.
    /// </summary>
    internal static bool IsEntityReuseOrderingDependency(
        ApolloOperationBatchExecutionNode dependentBatch,
        int index,
        IOperationPlanNode dependency,
        IReadOnlyDictionary<int, (ApolloOperationBatchExecutionNode Batch, int Index)> apolloDefinitions)
        => IsApolloEntityReuseOrderingDependency(
            ApolloLookupSignatureSource.From(dependentBatch, index),
            dependency,
            apolloDefinitions);

    /// <summary>
    /// Maps the id of every operation definition in an Apollo batch to its batch and index.
    /// </summary>
    internal static Dictionary<int, (ApolloOperationBatchExecutionNode Batch, int Index)> IndexApolloDefinitions(
        IEnumerable<IOperationPlanNode> nodes)
    {
        var map = new Dictionary<int, (ApolloOperationBatchExecutionNode, int)>();

        foreach (var node in nodes)
        {
            if (node is ApolloOperationBatchExecutionNode batch)
            {
                for (var i = 0; i < batch.Operations.Length; i++)
                {
                    map[batch.Operations[i].Id] = (batch, i);
                }
            }
        }

        return map;
    }

    private static bool IsApolloEntityReuseOrderingDependency(
        ApolloLookupSignatureSource dependent,
        IOperationPlanNode dependency,
        IReadOnlyDictionary<int, (ApolloOperationBatchExecutionNode Batch, int Index)>? apolloDefinitions = null)
    {
        if (!dependent.SelectsOnlyReusableFields())
        {
            return false;
        }

        var signature = dependent.ComputeSignature();

        switch (dependency)
        {
            case ApolloOperationExecutionNode apolloOperation:
                return signature.Equals(
                    ApolloLookupSignatureSource.From(apolloOperation).ComputeSignature(),
                    StringComparison.Ordinal);

            case ApolloOperationBatchExecutionNode apolloBatch:
                for (var i = 0; i < apolloBatch.Operations.Length; i++)
                {
                    if (signature.Equals(
                        ApolloLookupSignatureSource.From(apolloBatch, i).ComputeSignature(),
                        StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;

            case OperationDefinition definition
                when apolloDefinitions is not null
                && apolloDefinitions.TryGetValue(definition.Id, out var location)
                && ReferenceEquals(location.Batch.Operations[location.Index], definition):
                return signature.Equals(
                    ApolloLookupSignatureSource.From(location.Batch, location.Index).ComputeSignature(),
                    StringComparison.Ordinal);

            default:
                return false;
        }
    }

    /// <summary>
    /// The parts of an Apollo entity lookup that make two lookups interchangeable.
    /// </summary>
    private readonly record struct ApolloLookupSignatureSource(
        string SchemaName,
        ApolloEntityLookup Lookup,
        SelectionPath Target,
        OperationRequirement[] Requirements,
        string[] ForwardedVariables,
        ExecutionNodeCondition[] Conditions,
        ExecutionNodeCondition[][] ConditionAlternatives,
        ResultSelectionSet ResultSelectionSet)
    {
        public static ApolloLookupSignatureSource From(ApolloOperationExecutionNode node)
            => new(
                node.SchemaName,
                node.Lookup,
                node.Target,
                node.Requirements.ToArray(),
                node.ForwardedVariables.ToArray(),
                node.Conditions.ToArray(),
                node.ConditionAlternatives.ToArray(),
                node.ResultSelectionSet);

        public static ApolloLookupSignatureSource From(ApolloOperationBatchExecutionNode batch, int index)
        {
            var definition = batch.Operations[index];
            var target = definition switch
            {
                SingleOperationDefinition single => single.Target,
                BatchOperationDefinition merged => merged.Targets[0],
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            return new(
                batch.SchemaName,
                batch.Lookups[index],
                target,
                definition.Requirements.ToArray(),
                definition.ForwardedVariables.ToArray(),
                definition.Conditions.ToArray(),
                definition.ConditionAlternatives.ToArray(),
                definition.ResultSelectionSet);
        }

        public bool SelectsOnlyReusableFields()
            => EntityReuseCandidateMap.TryGetReusableFields(
                ResultSelectionSet,
                Requirements,
                ForwardedVariables.Length,
                Conditions.Length + ConditionAlternatives.Length,
                out _,
                out _);

        public string ComputeSignature()
        {
            var target = Target;
            var requirements = string.Join(
                ",",
                Requirements
                    .Select(r => $"{r.Type}:{r.Path.RelativeTo(target)}:{r.InternalAlias}:{r.Map}")
                    .Order(StringComparer.Ordinal));
            var conditions = FormatCanonicalConditions(Conditions);
            var conditionAlternatives = FormatCanonicalConditionAlternatives(ConditionAlternatives);
            var forwardedVariables = string.Join(",", ForwardedVariables.Order(StringComparer.Ordinal));
            var bodyText = FormatCanonicalSelectionSet(Lookup.OperationDocument, []);

            return $"{SchemaName}|{Lookup.EntityTypeName}|{conditions}|{conditionAlternatives}|{forwardedVariables}"
                + $"|{requirements}|{ResultSelectionSet.ToString(indented: false)}|{bodyText}";
        }
    }

    private static bool TryGetLookupKey(
        IOperationPlanNode node,
        out string? schemaName,
        [NotNullWhen(true)] out string? lookupTypeName)
    {
        (schemaName, lookupTypeName) = node switch
        {
            OperationExecutionNode operation => (operation.SchemaName, operation.LookupTypeName),
            OperationDefinition operation => (operation.SchemaName, operation.LookupTypeName),
            _ => (null, null)
        };

        return lookupTypeName is not null;
    }

    private static bool SelectsOnlyReusableFields(IOperationPlanNode node)
        => node switch
        {
            OperationExecutionNode operation => EntityReuseCandidateMap.TryGetReusableFields(
                operation.ResultSelectionSet,
                operation.Requirements,
                operation.ForwardedVariables.Length,
                operation.Conditions.Length + operation.ConditionAlternatives.Length,
                out _,
                out _),
            OperationDefinition operation => EntityReuseCandidateMap.TryGetReusableFields(
                operation.ResultSelectionSet,
                operation.Requirements,
                operation.ForwardedVariables.Length,
                operation.Conditions.Length + operation.ConditionAlternatives.Length,
                out _,
                out _),
            _ => false
        };

    private static string ComputeLookupSignature(IOperationPlanNode node)
        => node switch
        {
            OperationExecutionNode operation => ComputeCanonicalSignature(operation),
            OperationDefinition operation => ComputeCanonicalSignature(
                operation.SchemaName,
                operation.Source,
                operation.Document,
                operation.Requirements,
                operation.Conditions,
                operation.ConditionAlternatives,
                operation.DeliveryGroups),
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };

    /// <summary>
    /// Computes a signature that is equal for two Apollo entity lookups exactly when
    /// one lookup can fetch and complete the entities of the other lookup's target.
    /// </summary>
    private static string ComputeApolloCanonicalSignature(
        ExecutionPlanBuildContext ctx,
        ApolloOperationExecutionNode node)
    {
        if (!ctx.ApolloLookupOperationsByStepId.TryGetValue(node.Id, out var lookupOperation))
        {
            throw ThrowHelper.ApolloLookupOperationMissing(node.Id);
        }

        var replacements = BuildPrefixReplacements(node.Requirements);
        var lookupDocument = Utf8GraphQLOperationParser.Parse(lookupOperation.Value, ParserOptions.Trusted);
        var bodyText = FormatCanonicalSelectionSet(lookupDocument, replacements);
        var conditions = FormatCanonicalConditions(node.Conditions);
        var conditionAlternatives = FormatCanonicalConditionAlternatives(node.ConditionAlternatives);
        var requirements = string.Join(
            ",",
            node.Requirements.ToArray()
                .Select(r =>
                    $"{ApplyPrefixReplacements(r.Key, replacements)}:{r.Type}:"
                    + $"{r.Path.RelativeTo(node.Target)}:{r.InternalAlias}:{r.Map}")
                .Order(StringComparer.Ordinal));
        var forwardedVariables = string.Join(
            ",",
            node.ForwardedVariables.ToArray().Order(StringComparer.Ordinal));
        var resultSelectionSet = node.ResultSelectionSet.ToString(indented: false);

        return $"{node.SchemaName}|{node.Lookup.EntityTypeName}|{conditions}|{conditionAlternatives}|{forwardedVariables}"
            + $"|{requirements}|{resultSelectionSet}|{bodyText}";
    }

    private static string FormatCanonicalSelectionSet(
        Utf8OperationDocument document,
        ReadOnlySpan<(byte[] Original, byte[] Canonical)> replacements)
    {
        var operations = document.GetOperations().GetEnumerator();

        if (!operations.MoveNext())
        {
            throw ThrowHelper.SingleOperationRequired();
        }

        using var buffer = new PooledArrayWriter();
        operations.Current.SelectionSet.Format(buffer, indented: true);

        var normalized = ApplyPrefixReplacements(buffer.WrittenSpan, replacements);
        return Encoding.UTF8.GetString(normalized);
    }

    private static string FormatCanonicalConditions(ReadOnlySpan<ExecutionNodeCondition> conditions)
        => string.Join(",", conditions.ToArray()
            .OrderBy(c => c.VariableName, StringComparer.Ordinal)
            .Select(c => $"{c.VariableName}:{c.PassingValue}"));

    private static string FormatCanonicalConditionAlternatives(
        ReadOnlySpan<ExecutionNodeCondition[]> alternatives)
    {
        if (alternatives.IsEmpty)
        {
            return string.Empty;
        }

        var formatted = new string[alternatives.Length];

        for (var i = 0; i < formatted.Length; i++)
        {
            formatted[i] = FormatCanonicalConditions(alternatives[i]);
        }

        Array.Sort(formatted, StringComparer.Ordinal);
        return string.Join(";", formatted);
    }

    private static string ApplyPrefixReplacements(
        string text,
        ReadOnlySpan<(byte[] Original, byte[] Canonical)> replacements)
        => Encoding.UTF8.GetString(ApplyPrefixReplacements(Encoding.UTF8.GetBytes(text), replacements));

    private static (OperationSourceText operation, OperationRequirement[] requirements) CanonicalizeOperation(
        OperationExecutionNode node)
    {
        return (node.Operation, node.Requirements.ToArray());
    }

    /// <summary>
    /// Builds replacement pairs that normalize step-specific
    /// <c>__fusion_{N}_</c> variable name prefixes into a canonical
    /// form, so structurally identical operations produce matching text.
    /// </summary>
    private static (byte[] Original, byte[] Canonical)[] BuildPrefixReplacements(
        ReadOnlySpan<OperationRequirement> requirements)
    {
        var prefixToArgs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var req in requirements)
        {
            var key = req.Key;
            var lastUnderscore = key.LastIndexOf('_');

            if (lastUnderscore <= 0)
            {
                continue;
            }

            var prefix = key[..lastUnderscore];
            var arg = key[(lastUnderscore + 1)..];

            if (!prefixToArgs.TryGetValue(prefix, out var args))
            {
                args = new(StringComparer.Ordinal);
                prefixToArgs[prefix] = args;
            }

            args.Add(arg);
        }

        var sortedPrefixes = prefixToArgs
            .OrderBy(kvp => string.Join(",", kvp.Value), StringComparer.Ordinal)
            .Select(kvp => kvp.Key)
            .ToList();

        var result = new (byte[] Original, byte[] Canonical)[sortedPrefixes.Count];

        for (var i = 0; i < sortedPrefixes.Count; i++)
        {
            // Variable name prefixes and the canonical form are ASCII, so their
            // UTF-8 encoding matches the source text byte-for-byte.
            result[i] = (
                Encoding.UTF8.GetBytes($"{sortedPrefixes[i]}_"),
                Encoding.UTF8.GetBytes($"__fusion_{i}_"));
        }

        return result;
    }

    private static byte[] ApplyPrefixReplacements(
        ReadOnlySpan<byte> text,
        ReadOnlySpan<(byte[] Original, byte[] Canonical)> replacements)
    {
        var current = text.ToArray();

        foreach (var (original, canonical) in replacements)
        {
            current = ReplaceBytes(current, original, canonical);
        }

        return current;
    }

    /// <summary>
    /// Replaces every non-overlapping occurrence of <paramref name="original"/>
    /// with <paramref name="replacement"/>, scanning left to right without
    /// re-scanning the emitted replacement. This mirrors <see cref="string.Replace(string, string)"/>.
    /// </summary>
    private static byte[] ReplaceBytes(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> replacement)
    {
        var first = source.IndexOf(original);

        if (first < 0)
        {
            return source.ToArray();
        }

        // Count the occurrences so the destination buffer can be sized exactly.
        var count = 1;
        var rest = source[(first + original.Length)..];
        int next;

        while ((next = rest.IndexOf(original)) >= 0)
        {
            count++;
            rest = rest[(next + original.Length)..];
        }

        var result = new byte[source.Length + (count * (replacement.Length - original.Length))];
        var destination = result.AsSpan();
        var remaining = source;
        int index;

        while ((index = remaining.IndexOf(original)) >= 0)
        {
            remaining[..index].CopyTo(destination);
            destination = destination[index..];
            replacement.CopyTo(destination);
            destination = destination[replacement.Length..];
            remaining = remaining[(index + original.Length)..];
        }

        remaining.CopyTo(destination);

        return result;
    }

    /// <summary>
    /// Partitions structurally identical operations into groups that can
    /// each be safely merged. Two operations cannot share a group if one
    /// transitively depends on the other, because merging them would
    /// create a cycle in the dependency graph. The <paramref name="mergePolicy"/>
    /// further restricts which candidates may share a group based on their
    /// dependency depth.
    /// </summary>
    private static List<List<OperationExecutionNode>> PartitionIntoMergeableGroups(
        List<OperationExecutionNode> candidates,
        Dictionary<int, HashSet<int>> dependenciesByStepId,
        OperationMergePolicy mergePolicy)
    {
        // Pre-compute dependency depths when the policy needs them.
        Dictionary<int, int>? depthLookup = null;

        if (mergePolicy is OperationMergePolicy.Conservative
            or OperationMergePolicy.Balanced)
        {
            depthLookup = [];
            var recursionStack = new HashSet<int>();

            foreach (var candidate in candidates)
            {
                GetDependencyDepth(candidate.Id, dependenciesByStepId, depthLookup, recursionStack);
            }
        }

        var groups = new List<List<OperationExecutionNode>>();
        var visited = new HashSet<int>();

        foreach (var candidate in candidates)
        {
            var placed = false;

            foreach (var group in groups)
            {
                var canJoin = true;

                // Policy-specific depth checks (applied before the more
                // expensive transitive-reachability walk).
                if (depthLookup is not null)
                {
                    var candidateDepth = depthLookup[candidate.Id];
                    var referenceDepth = depthLookup[group[0].Id];

                    switch (mergePolicy)
                    {
                        case OperationMergePolicy.Conservative
                            when candidateDepth != referenceDepth:
                            canJoin = false;
                            break;

                        case OperationMergePolicy.Balanced
                            when Math.Abs(candidateDepth - referenceDepth) > 1:
                            canJoin = false;
                            break;
                    }
                }

                if (canJoin)
                {
                    foreach (var existing in group)
                    {
                        visited.Clear();

                        if (IsTransitivelyReachable(candidate.Id, existing.Id, dependenciesByStepId, visited))
                        {
                            canJoin = false;
                            break;
                        }

                        visited.Clear();

                        if (IsTransitivelyReachable(existing.Id, candidate.Id, dependenciesByStepId, visited))
                        {
                            canJoin = false;
                            break;
                        }
                    }
                }

                if (canJoin)
                {
                    group.Add(candidate);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                groups.Add([candidate]);
            }
        }

        return groups;
    }

    private static bool IsTransitivelyReachable(
        int fromId,
        int targetId,
        Dictionary<int, HashSet<int>> dependenciesByStepId,
        HashSet<int> visited)
    {
        if (!dependenciesByStepId.TryGetValue(fromId, out var dependencies))
        {
            return false;
        }

        foreach (var dependency in dependencies)
        {
            if (dependency == targetId)
            {
                return true;
            }

            if (visited.Add(dependency) && IsTransitivelyReachable(dependency, targetId, dependenciesByStepId, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static SelectionSetNode GetSelectionSetNodeFromPath(
        OperationDefinitionNode operationDefinition,
        SelectionPath path)
    {
        var current = operationDefinition.SelectionSet;

        if (path.IsRoot)
        {
            return current;
        }

        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            switch (segment.Kind)
            {
                case SelectionPathSegmentKind.InlineFragment:
                {
                    var selection = current.Selections
                        .OfType<InlineFragmentNode>()
                        .FirstOrDefault(s => s.TypeCondition?.Name.Value == segment.Name)
                        ?? throw new InvalidOperationException(
                            $"Inline fragment on type '{segment.Name}' not found at path segment {i}.");

                    current = selection.SelectionSet;
                    break;
                }
                case SelectionPathSegmentKind.Field:
                {
                    var selection = current.Selections
                        .OfType<FieldNode>()
                        .FirstOrDefault(s => s.Alias?.Value == segment.Name || s.Name.Value == segment.Name);

                    if (selection?.SelectionSet is null)
                    {
                        throw new InvalidOperationException(
                            $"Field '{segment.Name}' not found or has no selection set at path segment {i}.");
                    }

                    current = selection.SelectionSet;
                    break;
                }
            }
        }

        return current;
    }

    /// <summary>
    /// Strips child selection sets from fields whose return type is not a
    /// value type. Only value-type subtrees are relevant for the result
    /// selection set; the rest are resolved by separate execution nodes.
    /// </summary>
    private static SelectionSetNode PruneNonValueTypeChildren(
        SelectionSetNode selectionSet,
        ITypeDefinition parentType,
        FusionSchemaDefinition schema,
        string? sourceSchemaName,
        Dictionary<FieldNode, string>? sourceAliases,
        HashSet<SelectionSetNode>? sourceAliasSelectionSets)
    {
        var complexType = parentType as IComplexTypeDefinition;

        var changed = false;
        var selections = new ISelectionNode[selectionSet.Selections.Count];

        for (var i = 0; i < selectionSet.Selections.Count; i++)
        {
            var selection = selectionSet.Selections[i];

            switch (selection)
            {
                case FieldNode field when field.SelectionSet is not null:
                {
                    if (complexType is not null
                        && complexType.Fields.TryGetField(field.Name.Value, out var fieldDef))
                    {
                        var fieldNamedType = fieldDef.Type.NamedType();

                        if (fieldNamedType is FusionComplexTypeDefinition { IsValueType: true } valueType)
                        {
                            var pruned = PruneNonValueTypeChildren(
                                field.SelectionSet,
                                valueType,
                                schema,
                                sourceSchemaName,
                                sourceAliases,
                                sourceAliasSelectionSets);

                            if (!ReferenceEquals(pruned, field.SelectionSet))
                            {
                                var updated = new FieldNode(
                                    field.Name,
                                    field.Alias,
                                    field.Directives,
                                    field.Arguments,
                                    pruned);
                                CopySourceAlias(field, updated, sourceAliases);
                                selections[i] = updated;
                                changed = true;
                                continue;
                            }
                        }
                        else if (!IsOpaqueInterfaceObjectStandIn(fieldNamedType, sourceSchemaName))
                        {
                            // A non-value complex field (an entity boundary) is normally stripped
                            // because it is completed by a separate execution node. When its subtree
                            // reaches an @interfaceObject stand-in, however, the path to that field
                            // must be kept so the result selection set can carry the opacity marker;
                            // recurse to preserve it instead of stripping the whole subtree.
                            if (sourceAliasSelectionSets?.Contains(field.SelectionSet) == true
                                || (fieldNamedType is FusionComplexTypeDefinition complexFieldType
                                    && SubtreeContainsOpaqueStandIn(
                                        field.SelectionSet,
                                        complexFieldType,
                                        schema,
                                        sourceSchemaName)))
                            {
                                var pruned = PruneNonValueTypeChildren(
                                    field.SelectionSet,
                                    fieldNamedType,
                                    schema,
                                    sourceSchemaName,
                                    sourceAliases,
                                    sourceAliasSelectionSets);

                                var updated = new FieldNode(
                                    field.Name, field.Alias, field.Directives, field.Arguments, pruned);
                                CopySourceAlias(field, updated, sourceAliases);
                                selections[i] = updated;
                                changed = true;
                                continue;
                            }

                            var stripped = new FieldNode(
                                field.Name, field.Alias, field.Directives, field.Arguments, null);
                            CopySourceAlias(field, stripped, sourceAliases);
                            selections[i] = stripped;
                            changed = true;
                            continue;
                        }

                        // An @interfaceObject stand-in field keeps its interface-declared child
                        // selections so the result selection set can carry the opacity marker; the
                        // opaque value completes interface-typed against exactly those fields.
                    }

                    selections[i] = selection;
                    break;
                }

                case InlineFragmentNode inlineFragment:
                {
                    var fragmentType = inlineFragment.TypeCondition is not null
                        && schema.Types.TryGetType(
                            inlineFragment.TypeCondition.Name.Value,
                            allowInaccessibleFields: true,
                            out var resolvedType)
                            ? resolvedType
                            : parentType;

                    var pruned = PruneNonValueTypeChildren(
                        inlineFragment.SelectionSet,
                        fragmentType,
                        schema,
                        sourceSchemaName,
                        sourceAliases,
                        sourceAliasSelectionSets);

                    if (!ReferenceEquals(pruned, inlineFragment.SelectionSet))
                    {
                        selections[i] = new InlineFragmentNode(
                            inlineFragment.Location,
                            inlineFragment.TypeCondition,
                            inlineFragment.Directives,
                            pruned);
                        changed = true;
                        continue;
                    }

                    selections[i] = selection;
                    break;
                }

                default:
                    selections[i] = selection;
                    break;
            }
        }

        return changed ? selectionSet.WithSelections(selections) : selectionSet;
    }

    private static HashSet<SelectionSetNode>? CreateSourceAliasSelectionSetLookup(
        SelectionSetNode selectionSet,
        Dictionary<FieldNode, string>? sourceAliases)
    {
        if (sourceAliases is null)
        {
            return null;
        }

        var selectionSets = new HashSet<SelectionSetNode>(ReferenceEqualityComparer.Instance);
        Collect(selectionSet);
        return selectionSets;

        bool Collect(SelectionSetNode current)
        {
            var containsAlias = false;

            foreach (var selection in current.Selections)
            {
                switch (selection)
                {
                    case FieldNode field:
                        containsAlias |= sourceAliases.ContainsKey(field);
                        if (field.SelectionSet is not null)
                        {
                            containsAlias |= Collect(field.SelectionSet);
                        }
                        break;

                    case InlineFragmentNode inlineFragment:
                        containsAlias |= Collect(inlineFragment.SelectionSet);
                        break;
                }
            }

            if (containsAlias)
            {
                selectionSets.Add(current);
            }

            return containsAlias;
        }
    }

    private static void CopySourceAlias(
        FieldNode source,
        FieldNode target,
        Dictionary<FieldNode, string>? sourceAliases)
    {
        if (sourceAliases is not null && sourceAliases.TryGetValue(source, out var alias))
        {
            sourceAliases.TryAdd(target, alias);
        }
    }

    private static bool IsOpaqueInterfaceObjectStandIn(ITypeDefinition namedType, string? sourceSchemaName)
        => sourceSchemaName is not null
            && namedType is FusionInterfaceTypeDefinition interfaceType
            && interfaceType.Sources.TryGetMember(sourceSchemaName, out var source)
            && source.IsInterfaceObject;

    // Reports whether a selection set reaches an @interfaceObject stand-in field anywhere in its
    // subtree. Used to decide whether a non-value entity boundary must keep its child selections so
    // the result selection set can carry the opacity marker for a nested stand-in value.
    private static bool SubtreeContainsOpaqueStandIn(
        SelectionSetNode selectionSet,
        ITypeDefinition parentType,
        FusionSchemaDefinition schema,
        string? sourceSchemaName)
    {
        if (parentType is not IComplexTypeDefinition complexType)
        {
            return false;
        }

        foreach (var selection in selectionSet.Selections)
        {
            if (selection is FieldNode { SelectionSet: not null } field)
            {
                if (complexType.Fields.TryGetField(field.Name.Value, out var fieldDef))
                {
                    var fieldNamedType = fieldDef.Type.NamedType();

                    if (IsOpaqueInterfaceObjectStandIn(fieldNamedType, sourceSchemaName)
                        || (fieldNamedType is FusionComplexTypeDefinition complexFieldType
                            && SubtreeContainsOpaqueStandIn(
                                field.SelectionSet, complexFieldType, schema, sourceSchemaName)))
                    {
                        return true;
                    }
                }
            }
            else if (selection is InlineFragmentNode inlineFragment)
            {
                var fragmentType = inlineFragment.TypeCondition is not null
                    && schema.Types.TryGetType(
                        inlineFragment.TypeCondition.Name.Value,
                        allowInaccessibleFields: true,
                        out var resolvedType)
                        ? resolvedType
                        : parentType;

                if (SubtreeContainsOpaqueStandIn(
                        inlineFragment.SelectionSet, fragmentType, schema, sourceSchemaName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool DoVariablesContainUploadScalar(
        IReadOnlyList<VariableDefinitionNode> variables,
        FusionSchemaDefinition schema)
    {
        var inputObjectTypes = new Queue<IInputObjectTypeDefinition>();
        var visited = new HashSet<IInputObjectTypeDefinition>(ReferenceEqualityComparer.Instance);

        foreach (var variable in variables)
        {
            var variableTypeName = variable.Type.NamedType().Name.Value;

            if (!schema.Types.TryGetType(variableTypeName, allowInaccessibleFields: true, out var variableType))
            {
                continue;
            }

            if (variableType is IScalarTypeDefinition { Name: UploadScalarName })
            {
                return true;
            }

            if (variableType is IInputObjectTypeDefinition inputObjectType && visited.Add(inputObjectType))
            {
                inputObjectTypes.Enqueue(inputObjectType);
            }
        }

        while (inputObjectTypes.TryDequeue(out var inputObjectType))
        {
            foreach (var field in inputObjectType.Fields)
            {
                var fieldType = field.Type.NamedType();

                if (fieldType is IScalarTypeDefinition { Name: UploadScalarName })
                {
                    return true;
                }

                if (fieldType is IInputObjectTypeDefinition nestedInputObjectType && visited.Add(nestedInputObjectType))
                {
                    inputObjectTypes.Enqueue(nestedInputObjectType);
                }
            }
        }

        return false;
    }

    private static OperationDefinitionNode RemoveEmptySelections(OperationDefinitionNode operationDefinition)
    {
        return SyntaxRewriter.Create(
                rewrite: node =>
                {
                    if (node is not SelectionSetNode selectionSet)
                    {
                        return node;
                    }

                    List<ISelectionNode>? rewritten = null;
                    var selections = selectionSet.Selections;

                    for (var i = 0; i < selections.Count; i++)
                    {
                        var selection = selections[i];
                        var removeSelection =
                            selection is FieldNode { SelectionSet.Selections.Count: 0 }
                                or InlineFragmentNode { SelectionSet.Selections.Count: 0 };

                        if (!removeSelection)
                        {
                            rewritten?.Add(selection);
                            continue;
                        }

                        if (rewritten is null)
                        {
                            rewritten = new List<ISelectionNode>(selections.Count - 1);
                            for (var j = 0; j < i; j++)
                            {
                                rewritten.Add(selections[j]);
                            }
                        }
                    }

                    return rewritten is null
                        ? node
                        : new SelectionSetNode(rewritten);
                })
            .Rewrite(operationDefinition)!;
    }

    /// <summary>
    /// Removes gateway-internal executable directives from an operation before it is serialized
    /// for a source schema. Source schemas do not understand these directives and reject any
    /// request that carries them. The caller must pass a copy: the original plan step definition
    /// keeps the markers so the result reader can still identify requirement-only selections.
    /// </summary>
    /// <remarks>
    /// Two internal directives can occur on outgoing selections:
    /// <c>fusion__requirement</c> marks requirement-only selections; the directive is stripped while
    /// the field is kept, because the data still has to be fetched. <c>fusion__empty</c> is a
    /// synthesized <c>__typename</c> placeholder for an otherwise-empty selection set; the placeholder
    /// is dropped when the set has real siblings, otherwise the directive is stripped so a plain
    /// <c>__typename</c> keeps the selection set valid.
    /// </remarks>
    private static OperationDefinitionNode RemoveInternalDirectives(OperationDefinitionNode operationDefinition)
    {
        return SyntaxRewriter.Create(
                rewrite: node =>
                {
                    if (node is not SelectionSetNode selectionSet)
                    {
                        return node;
                    }

                    var selections = selectionSet.Selections;
                    List<ISelectionNode>? rewritten = null;

                    for (var i = 0; i < selections.Count; i++)
                    {
                        var selection = selections[i];
                        var replacement = selection;
                        var drop = false;

                        switch (selection)
                        {
                            case FieldNode
                            {
                                Alias: null,
                                Name.Value: IntrospectionFieldNames.TypeName,
                                Directives: [{ Name.Value: "fusion__empty" }]
                            } placeholder:
                                if (selections.Count > 1)
                                {
                                    drop = true;
                                }
                                else
                                {
                                    replacement = placeholder.WithDirectives([]);
                                }

                                break;

                            case FieldNode { Directives.Count: > 0 } field
                                when TryRemoveRequirementDirective(field.Directives, out var fieldDirectives):
                                replacement = field.WithDirectives(fieldDirectives);
                                break;

                            case InlineFragmentNode { Directives.Count: > 0 } fragment
                                when TryRemoveRequirementDirective(fragment.Directives, out var fragmentDirectives):
                                replacement = fragment.WithDirectives(fragmentDirectives);
                                break;
                        }

                        if (rewritten is null)
                        {
                            if (!drop && ReferenceEquals(replacement, selection))
                            {
                                continue;
                            }

                            rewritten = new List<ISelectionNode>(selections.Count);
                            for (var j = 0; j < i; j++)
                            {
                                rewritten.Add(selections[j]);
                            }
                        }

                        if (!drop)
                        {
                            rewritten.Add(replacement);
                        }
                    }

                    return rewritten is null
                        ? node
                        : new SelectionSetNode(rewritten);
                })
            .Rewrite(operationDefinition)!;
    }

    private static bool TryRemoveRequirementDirective(
        IReadOnlyList<DirectiveNode> directives,
        out IReadOnlyList<DirectiveNode> result)
    {
        for (var i = 0; i < directives.Count; i++)
        {
            if (directives[i].Name.Value.Equals("fusion__requirement", StringComparison.Ordinal))
            {
                var remaining = new List<DirectiveNode>(directives.Count - 1);

                for (var j = 0; j < directives.Count; j++)
                {
                    if (!directives[j].Name.Value.Equals("fusion__requirement", StringComparison.Ordinal))
                    {
                        remaining.Add(directives[j]);
                    }
                }

                result = remaining;
                return true;
            }
        }

        result = directives;
        return false;
    }

    /// <summary>
    /// Promotes the @skip and @include conditions shared by every selection of the step to node
    /// conditions and derives the condition alternatives from the remaining per-selection conditions.
    /// Returns <see langword="false"/> when there are neither, in which case <paramref name="updated"/>
    /// is <see langword="null"/> and the step must be used as is.
    /// </summary>
    private static bool TryExtractCommonConditionsAndRewrite(
        OperationPlanStep step,
        [NotNullWhen(true)] out OperationPlanStep? updated)
    {
        updated = null;

        var targetSelectionSet = step.Definition.SelectionSet;
        List<FieldNode>? enclosingFields = null;

        if (step.Lookup is not null)
        {
            enclosingFields = [];

            foreach (var pathFieldName in step.Lookup.Path)
            {
                if (!TryFindFieldWithSelectionSet(targetSelectionSet, pathFieldName, out var pathField))
                {
                    return false;
                }

                enclosingFields.Add(pathField);
                targetSelectionSet = pathField.SelectionSet!;
            }

            if (!TryFindFieldWithSelectionSet(targetSelectionSet, step.Lookup.FieldName, out var lookupField))
            {
                return false;
            }

            enclosingFields.Add(lookupField);
            targetSelectionSet = lookupField.SelectionSet!;
        }

        var leafConditions = new List<HashSet<ExecutionNodeCondition>>();
        CollectLeafConditions(targetSelectionSet, [], leafConditions);

        if (leafConditions.Count == 0)
        {
            return false;
        }

        var commonConditions = new HashSet<ExecutionNodeCondition>(leafConditions[0]);

        for (var i = 1; i < leafConditions.Count && commonConditions.Count > 0; i++)
        {
            commonConditions.IntersectWith(leafConditions[i]);
        }

        var conditionAlternatives = BuildConditionAlternatives(leafConditions, commonConditions);

        if (commonConditions.Count == 0 && conditionAlternatives.Length == 0)
        {
            return false;
        }

        var newSelectionSet = step.Definition.SelectionSet;

        if (commonConditions.Count > 0)
        {
            newSelectionSet = RewriteConditionalSelectionSet(targetSelectionSet, commonConditions);

            if (enclosingFields is not null)
            {
                for (var i = enclosingFields.Count - 1; i >= 0; i--)
                {
                    var enclosingField = enclosingFields[i];
                    var parentSelectionSet = i == 0
                        ? step.Definition.SelectionSet
                        : enclosingFields[i - 1].SelectionSet!;

                    newSelectionSet = ReplaceSelection(
                        parentSelectionSet,
                        enclosingField,
                        enclosingField.WithSelectionSet(newSelectionSet));
                }
            }
        }

        foreach (var existing in step.Conditions)
        {
            commonConditions.Add(existing);
        }

        updated = step with
        {
            Definition = step.Definition.WithSelectionSet(newSelectionSet),
            Conditions = SortConditions(commonConditions),
            ConditionAlternatives = conditionAlternatives
        };

        return true;
    }

    /// <summary>
    /// Builds the condition alternatives from the conditions that gate each leaf selection
    /// without the common conditions. Returns an empty array when a leaf selection has no
    /// remaining condition or when the alternatives contain a pair that always passes.
    /// </summary>
    private static ExecutionNodeCondition[][] BuildConditionAlternatives(
        List<HashSet<ExecutionNodeCondition>> leafConditions,
        HashSet<ExecutionNodeCondition> commonConditions)
    {
        // The common conditions are a subset of every leaf set, so a leaf has no remaining
        // condition exactly when its set is as large as the common set.
        foreach (var leaf in leafConditions)
        {
            if (leaf.Count == commonConditions.Count)
            {
                return [];
            }
        }

        var residuals = new List<(string Key, ExecutionNodeCondition[] Conditions)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var leaf in leafConditions)
        {
            var residual = new HashSet<ExecutionNodeCondition>(leaf);
            residual.ExceptWith(commonConditions);

            var sorted = SortConditions(residual);
            var key = FormatCanonicalConditions(sorted);

            if (keys.Add(key))
            {
                residuals.Add((key, sorted));
            }
        }

        var alternatives = new List<(string Key, ExecutionNodeCondition[] Conditions)>(residuals.Count);

        foreach (var candidate in residuals)
        {
            var isRedundant = false;

            foreach (var other in residuals)
            {
                if (other.Conditions.Length < candidate.Conditions.Length
                    && IsSubset(other.Conditions, candidate.Conditions))
                {
                    isRedundant = true;
                    break;
                }
            }

            if (!isRedundant)
            {
                alternatives.Add(candidate);
            }
        }

        if (HasComplementaryPair(alternatives))
        {
            return [];
        }

        alternatives.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        var result = new ExecutionNodeCondition[alternatives.Count][];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = alternatives[i].Conditions;
        }

        return result;

        static bool HasComplementaryPair(List<(string Key, ExecutionNodeCondition[] Conditions)> alternatives)
        {
            for (var i = 0; i < alternatives.Count; i++)
            {
                var first = alternatives[i].Conditions;

                if (first.Length != 1)
                {
                    continue;
                }

                for (var j = i + 1; j < alternatives.Count; j++)
                {
                    var second = alternatives[j].Conditions;

                    if (second.Length == 1
                        && first[0].VariableName == second[0].VariableName
                        && first[0].PassingValue != second[0].PassingValue)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool IsSubset(ExecutionNodeCondition[] subset, ExecutionNodeCondition[] superset)
        {
            foreach (var condition in subset)
            {
                if (Array.IndexOf(superset, condition) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static ExecutionNodeCondition[] SortConditions(HashSet<ExecutionNodeCondition> conditions)
        => conditions
            .OrderBy(c => c.VariableName, StringComparer.Ordinal)
            .ThenBy(c => c.PassingValue)
            .ToArray();

    private static bool TryFindFieldWithSelectionSet(
        SelectionSetNode selectionSetNode,
        string fieldName,
        [NotNullWhen(true)] out FieldNode? field)
    {
        foreach (var selection in selectionSetNode.Selections)
        {
            if (selection is FieldNode { SelectionSet: not null } fieldNode
                && fieldNode.Name.Value == fieldName)
            {
                field = fieldNode;
                return true;
            }
        }

        field = null;
        return false;
    }

    private static SelectionSetNode ReplaceSelection(
        SelectionSetNode selectionSetNode,
        ISelectionNode original,
        ISelectionNode replacement)
    {
        var selections = new List<ISelectionNode>(selectionSetNode.Selections.Count);

        foreach (var selection in selectionSetNode.Selections)
        {
            selections.Add(ReferenceEquals(selection, original) ? replacement : selection);
        }

        return selectionSetNode.WithSelections(selections);
    }

    /// <summary>
    /// Collects for every leaf selection the set of conditions that gate it, including
    /// the conditions of the enclosing untyped inline fragments.
    /// </summary>
    private static void CollectLeafConditions(
        SelectionSetNode selectionSetNode,
        List<ExecutionNodeCondition> ancestorConditions,
        List<HashSet<ExecutionNodeCondition>> leafConditions)
    {
        foreach (var selection in selectionSetNode.Selections)
        {
            switch (selection)
            {
                case FieldNode fieldNode:
                    leafConditions.Add(CreateConditionSet(ancestorConditions, fieldNode.Directives));
                    break;

                case InlineFragmentNode { TypeCondition: null } untypedFragment:
                    var conditions = ExtractConditions(untypedFragment.Directives);
                    var restoreCount = ancestorConditions.Count;

                    if (conditions is not null)
                    {
                        ancestorConditions.AddRange(conditions);
                    }

                    CollectLeafConditions(
                        untypedFragment.SelectionSet,
                        ancestorConditions,
                        leafConditions);

                    ancestorConditions.RemoveRange(
                        restoreCount,
                        ancestorConditions.Count - restoreCount);
                    break;

                case InlineFragmentNode typedFragment:
                    leafConditions.Add(CreateConditionSet(ancestorConditions, typedFragment.Directives));
                    break;
            }
        }

        static HashSet<ExecutionNodeCondition> CreateConditionSet(
            List<ExecutionNodeCondition> ancestorConditions,
            IReadOnlyList<DirectiveNode> directives)
        {
            var conditionSet = new HashSet<ExecutionNodeCondition>(ancestorConditions);
            var conditions = ExtractConditions(directives);

            if (conditions is not null)
            {
                foreach (var condition in conditions)
                {
                    conditionSet.Add(condition);
                }
            }

            return conditionSet;
        }
    }

    /// <summary>
    /// Removes the @skip and @include directives that represent one of the common
    /// conditions and leaves all other directives in place.
    /// </summary>
    private static SelectionSetNode RewriteConditionalSelectionSet(
        SelectionSetNode selectionSetNode,
        HashSet<ExecutionNodeCondition> commonConditions)
    {
        var selections = new List<ISelectionNode>();

        foreach (var selection in selectionSetNode.Selections)
        {
            switch (selection)
            {
                case FieldNode fieldNode:
                {
                    if (TryRemoveCommonConditions(fieldNode.Directives, commonConditions, out var newDirectives))
                    {
                        fieldNode = fieldNode.WithDirectives(newDirectives);
                    }

                    selections.Add(fieldNode);
                    break;
                }
                case InlineFragmentNode inlineFragmentNode:
                {
                    if (inlineFragmentNode.TypeCondition is null)
                    {
                        var fragmentSelectionSet =
                            RewriteConditionalSelectionSet(inlineFragmentNode.SelectionSet, commonConditions);

                        if (fragmentSelectionSet.Selections.Count == 0)
                        {
                            continue;
                        }

                        if (TryRemoveCommonConditions(
                            inlineFragmentNode.Directives,
                            commonConditions,
                            out var newDirectives))
                        {
                            if (newDirectives.Count == 0)
                            {
                                selections.AddRange(fragmentSelectionSet.Selections);
                                continue;
                            }

                            inlineFragmentNode = inlineFragmentNode.WithDirectives(newDirectives);
                        }

                        inlineFragmentNode = inlineFragmentNode.WithSelectionSet(fragmentSelectionSet);
                    }

                    selections.Add(inlineFragmentNode);
                    break;
                }
            }
        }

        return new SelectionSetNode(selections);
    }

    private static bool TryRemoveCommonConditions(
        IReadOnlyList<DirectiveNode> directives,
        HashSet<ExecutionNodeCondition> commonConditions,
        [NotNullWhen(true)] out List<DirectiveNode>? newDirectives)
    {
        var conditions = ExtractConditions(directives);

        if (conditions is null)
        {
            newDirectives = null;
            return false;
        }

        List<DirectiveNode>? result = null;

        foreach (var condition in conditions)
        {
            if (commonConditions.Contains(condition))
            {
                result ??= [.. directives];
                result.Remove(condition.Directive!);
            }
        }

        newDirectives = result;
        return result is not null;
    }

    /// <summary>
    /// Mutable state for building the execution nodes of a plan.
    /// <paramref name="nextNodeId"/> must be greater than every identifier
    /// already used by the plan steps.
    /// </summary>
    private sealed class ExecutionPlanBuildContext(int nextNodeId)
    {
        private int _nextNodeId = nextNodeId;

        public HashSet<int> ProcessedStepIds { get; } = [];
        public Dictionary<int, ExecutionNode> ExecutionNodes { get; } = [];
        public Dictionary<int, OperationSourceText> ApolloLookupOperationsByStepId { get; } = [];
        public Dictionary<int, HashSet<int>> DependenciesByStepId { get; } = [];
        public Dictionary<int, Dictionary<string, int>> BranchesByNodeId { get; } = [];
        public Dictionary<int, int> FallbackByNodeId { get; } = [];
        public Dictionary<int, int> RedirectedStepIds { get; } = [];

        /// <summary>
        /// The delivery groups of a plan with single plan defer, indexed by group id, or the default
        /// array when the plan does not use them.
        /// </summary>
        public ImmutableArray<DeliveryGroup> DeliveryGroups { get; init; }

        /// <summary>
        /// Returns a node identifier that is unique across all execution nodes
        /// and operation definitions of the plan being built.
        /// </summary>
        public int NextId() => _nextNodeId++;
    }

    private readonly record struct MergeResult(
        SelectionPath[] Targets,
        OperationSourceText CanonicalOp,
        OperationRequirement[] CanonicalRequirements,
        OperationExecutionNode Primary,
        ImmutableArray<int> DeliveryGroups);

    /// <summary>
    /// Structurally identical Apollo lookups that merge into one lookup. The node with the
    /// lowest identifier is the primary node that represents the group in the plan.
    /// </summary>
    private sealed class ApolloLookupGroup(int depth)
    {
        public int Depth { get; } = depth;

        public int MinDepth { get; set; } = depth;

        public List<ApolloOperationExecutionNode> Nodes { get; } = [];
    }
}

file static class Extensions
{
    public static bool IsIntrospectionOnly(this Operation operation)
    {
        var hasNonInternalIntrospectionSelection = false;

        foreach (var selection in operation.RootSelectionSet.Selections)
        {
            if (selection.IsInternal)
            {
                continue;
            }

            if (selection.Field.IsIntrospectionField)
            {
                hasNonInternalIntrospectionSelection = true;
                continue;
            }

            return false;
        }

        return hasNonInternalIntrospectionSelection;
    }

    public static bool HasIntrospectionFields(this Operation operation)
    {
        foreach (var selection in operation.RootSelectionSet.Selections)
        {
            if (selection is { IsInternal: false, Field.IsIntrospectionField: true })
            {
                return true;
            }
        }

        return false;
    }

    public static Selection[] GetIntrospectionSelections(this Operation operation)
    {
        var selections = new List<Selection>(operation.RootSelectionSet.Selections.Length);

        foreach (var selection in operation.RootSelectionSet.Selections)
        {
            if (selection is { IsInternal: false, Field.IsIntrospectionField: true })
            {
                selections.Add(selection);
            }
        }

        return selections.ToArray();
    }

    public static OperationSourceText ToSourceText(this OperationDefinitionNode operation)
        => OperationSourceText.Create(operation.Name!.Value, operation.Operation, operation);
}
