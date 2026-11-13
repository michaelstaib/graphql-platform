using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Language;
using HotChocolate.Fusion.Planning.Partitioners;
using HotChocolate.Fusion.Types;
using HotChocolate.Fusion.Types.Rewriters;
using HotChocolate.Language;
using HotChocolate.Types;
using ThrowHelper = HotChocolate.Fusion.Execution.ThrowHelper;

namespace HotChocolate.Fusion.Planning;

public sealed partial class OperationPlanner
{
    /// <summary>
    /// Plans each <see cref="IncrementalPlanDescriptor"/> and records any
    /// parent-scope requirements needed by its incremental plan.
    /// </summary>
    private ImmutableArray<DeferRoutingState> RouteIncrementalPlans(
        string id,
        DeferSplitResult splitResult,
        PlanContextGraph contextGraph,
        PlanningBudget budget,
        CancellationToken cancellationToken)
    {
        if (splitResult.IncrementalPlanDescriptors.IsEmpty)
        {
            return [];
        }

        var routingStates = ImmutableArray.CreateBuilder<DeferRoutingState>(
            splitResult.IncrementalPlanDescriptors.Length);

        // The not-deferred optimization below only ever drops a leaf descriptor (see
        // TryAbsorbFullyRedundantDefer); a descriptor with nested @defer children keeps
        // its own registered scope so those children can resolve their enclosing context.
        var descriptorsWithChildren = new HashSet<IncrementalPlanDescriptor>();
        foreach (var descriptorWithParent in splitResult.IncrementalPlanDescriptors)
        {
            if (descriptorWithParent.Parent is { } parentDescriptor)
            {
                descriptorsWithChildren.Add(parentDescriptor);
            }
        }

        for (var i = 0; i < splitResult.IncrementalPlanDescriptors.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descriptor = splitResult.IncrementalPlanDescriptors[i];

            var parentContext = contextGraph.GetParentContext(descriptor);
            var incrementalPlanResult = PlanIncrementalPlan(
                id,
                descriptor,
                i,
                budget,
                cancellationToken);

            var rewrittenIncrementalPlan = ApplyDeferRequirementsToParent(
                descriptor,
                incrementalPlanResult.Steps,
                parentContext,
                contextGraph);

            // A mutation-typed step surviving ApplyDeferRequirementsToParent carries real
            // deferred output of its own, meaning the only way to have produced it was to
            // invoke the mutation's root field a second time inside this incremental plan.
            if (descriptor.Operation.Operation != OperationType.Query)
            {
                foreach (var step in rewrittenIncrementalPlan)
                {
                    if (step is OperationPlanStep { Definition.Operation: OperationType.Mutation })
                    {
                        var anchorTypeName = TryLocateIncrementalPlanAnchor(
                            descriptor.Operation,
                            descriptor.Path,
                            out _,
                            out var anchorType)
                                ? anchorType.Name
                                : _schema.GetOperationType(descriptor.Operation.Operation).Name;

                        throw new DeferredMutationLookupRequiredException(descriptor.Path, anchorTypeName);
                    }
                }
            }

            // When every field this descriptor would deliver incrementally is already
            // available from the enclosing scope (the anchor's own key fields, or a field
            // already fetched there as a requirement for an unrelated reason), the spec
            // allows serving it in the initial payload instead: the fields are made
            // visible on the enclosing step and no incremental plan is produced for this
            // descriptor. Restricted to leaf descriptors; see descriptorsWithChildren.
            if (!descriptorsWithChildren.Contains(descriptor)
                && TryAbsorbFullyRedundantDefer(
                    descriptor,
                    rewrittenIncrementalPlan,
                    contextGraph.GetParentContext(descriptor),
                    contextGraph))
            {
                continue;
            }

            EnsureSelectedFieldsHaveNodes(descriptor, rewrittenIncrementalPlan);

            var registeredInternalOp = incrementalPlanResult.InternalOperationDefinition ?? descriptor.Operation;
            contextGraph.RegisterDeferContext(
                descriptor,
                rewrittenIncrementalPlan,
                SelectionSetIndexer.Create(registeredInternalOp),
                registeredInternalOp);

            routingStates.Add(new DeferRoutingState(descriptor, i));
        }

        return routingStates.ToImmutable();
    }

    /// <summary>
    /// Throws when <paramref name="descriptor"/> selects fields but its incremental plan has no steps.
    /// </summary>
    internal void EnsureSelectedFieldsHaveNodes(
        IncrementalPlanDescriptor descriptor,
        ImmutableList<PlanStep> incrementalPlanSteps)
    {
        if (incrementalPlanSteps.Count == 0 && SelectsDeferredFields(descriptor))
        {
            throw ThrowHelper.DeferredGroupPlannedToNothing(descriptor.Path);
        }
    }

    private bool SelectsDeferredFields(IncrementalPlanDescriptor descriptor)
    {
        if (descriptor.Path.IsRoot)
        {
            return ContainsSelectedField(descriptor.Operation.SelectionSet);
        }

        return TryLocateIncrementalPlanAnchor(
                descriptor.Operation,
                descriptor.Path,
                out var anchorSelectionSet,
                out _)
            && ContainsSelectedField(anchorSelectionSet);
    }

    private static bool ContainsSelectedField(SelectionSetNode selectionSet)
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field
                    when !field.Directives.Any(
                        t => t.Name.Value.Equals("fusion__requirement", StringComparison.Ordinal)
                            || t.Name.Value.Equals("fusion__empty", StringComparison.Ordinal)):
                    if (field.SelectionSet is null || ContainsSelectedField(field.SelectionSet))
                    {
                        return true;
                    }

                    break;

                case InlineFragmentNode fragment when ContainsSelectedField(fragment.SelectionSet):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the incremental plans for the routed descriptors.
    /// </summary>
    private ImmutableArray<IncrementalPlan> BuildIncrementalPlans(
        string id,
        string hash,
        string shortHash,
        ImmutableArray<DeferRoutingState> routingStates,
        PlanContextGraph contextGraph,
        CancellationToken cancellationToken)
    {
        if (routingStates.IsDefaultOrEmpty)
        {
            return [];
        }

        var incrementalPlansBuilder = ImmutableArray.CreateBuilder<IncrementalPlan>(routingStates.Length);

        foreach (var routingState in routingStates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descriptor = routingState.Descriptor;

            // Use the registered scope state because nested descriptors may
            // have added parent-scope requirements.
            var finalSteps = contextGraph.GetRegisteredSteps(descriptor);
            var registeredInternalOp = contextGraph.GetRegisteredInternalOperation(descriptor);

            var (rootNodes, allNodes) = BuildDeferredExecutionNodes(
                registeredInternalOp,
                finalSteps,
                finalSteps.NextId(),
                cancellationToken);

            var compiledOp = AddTypeNameToAbstractSelections(
                registeredInternalOp,
                _schema.GetOperationType(registeredInternalOp.Operation));
            var deferredOperation = _operationCompiler.Compile(
                id + "#defer_" + routingState.Index,
                hash + "#defer_" + routingState.Index,
                shortHash,
                compiledOp);

            var planScopeRequirements = descriptor.Requirements.Count == 0
                ? ImmutableArray<OperationRequirement>.Empty
                : [.. descriptor.Requirements.Values];

            var incrementalPlan = new IncrementalPlan(
                deferredOperation,
                rootNodes,
                allNodes,
                descriptor.DeliveryGroupSet,
                planScopeRequirements,
                [.. descriptor.HeldFields]);

            incrementalPlansBuilder.Add(incrementalPlan);
        }

        return incrementalPlansBuilder.ToImmutable();
    }

    /// <summary>
    /// Captures the routed descriptor order used when building incremental plans.
    /// </summary>
    private readonly record struct DeferRoutingState(
        IncrementalPlanDescriptor Descriptor,
        int Index);

    /// <summary>
    /// Plans a single incremental plan descriptor.
    /// </summary>
    private DeferIncrementalPlanResult PlanIncrementalPlan(
        string operationId,
        IncrementalPlanDescriptor descriptor,
        int incrementalPlanId,
        PlanningBudget budget,
        CancellationToken cancellationToken)
    {
        var deferredOperation = descriptor.Operation;
        var isMutation = deferredOperation.Operation != OperationType.Query;

        var index = SelectionSetIndexer.Create(deferredOperation);

        var possiblePlans = new PlanQueue(_schema);
        PlanNode node;

        // The anchor type for the mutation, non-root branch below, used to report
        // DeferredMutationLookupRequiredException when no incremental plan is found.
        ITypeDefinition? mutationAnchorType = null;

        if (descriptor.Path.IsRoot)
        {
            if (isMutation)
            {
                // Deferring the operation's own top-level field(s) leaves nothing to key a
                // lookup off of, so producing them would require re-running the mutation root
                // field a second time. Fail explicitly instead of duplicating its side effects.
                throw new DeferredMutationLookupRequiredException(
                    descriptor.Path,
                    _schema.GetOperationType(deferredOperation.Operation).Name);
            }

            // A defer anchored at the operation root has nothing to key a lookup off of;
            // it is planned like a normal root-rooted operation.
            SelectionSet selectionSet;
            (node, selectionSet) = CreateQueryPlanBase(deferredOperation, "defer", index);

            // Nothing to fetch: the deferred operation selects no field that needs a source.
            if (node.Backlog.IsEmpty)
            {
                return new DeferIncrementalPlanResult([], null);
            }

            foreach (var (schemaName, resolutionCost) in _schema.GetPossibleSchemas(selectionSet))
            {
                possiblePlans.Enqueue(
                    node with
                    {
                        SchemaName = schemaName,
                        ResolutionCost = resolutionCost
                    });
            }
        }
        else if (TryLocateNodeFieldAnchor(deferredOperation, descriptor.Path, out var nodeFieldNode))
        {
            // A defer anchored directly on the root's `node(id: ...)` field cannot be
            // planned as an ordinary keyed lookup: `Node` is not an entity with its own
            // lookup, its concrete type is only known once the id is decoded, and that
            // decoding, per-concrete-type schema resolution, and shared-selection
            // handling all live in the dedicated NodeFieldWorkItem path that
            // CreateQueryPlanBase also uses for a root-anchored `node` selection. Reuse
            // that same path here instead of seeding a Lookup work item for the `Node`
            // interface, which the ordinary lookup machinery cannot serve.
            var backlog = Backlog.Empty.Push(
                new NodeFieldWorkItem(new NodeField { Field = nodeFieldNode, ParentFragments = null }));

            node = CreateIncrementalPlanNode(deferredOperation, index, backlog);
            possiblePlans.EnqueueBranches(node);
        }
        else
        {
            if (!TryLocateIncrementalPlanAnchor(
                deferredOperation,
                descriptor.Path,
                out var anchorSelectionSet,
                out var anchorType))
            {
                throw new InvalidOperationException(
                    $"Unable to locate the defer anchor selection set at path '{descriptor.Path}' "
                    + "inside the incremental plan operation.");
            }

            if (isMutation)
            {
                mutationAnchorType = anchorType;

                // A defer anchored inside a mutation's own result cannot walk back to the
                // operation root to source a lookup key, so the incremental plan is seeded as
                // a normal mutation root fetch instead. The resulting key-only producer step is
                // later dropped and routed to the parent scope so the mutation runs exactly once.
                SelectionSet mutationSelectionSet;
                (node, mutationSelectionSet) = CreateMutationPlanBase(deferredOperation, "defer", index);

                // Nothing to fetch: the deferred operation selects no field that needs a source.
                if (node.Backlog.IsEmpty)
                {
                    return new DeferIncrementalPlanResult([], null);
                }

                foreach (var (schemaName, resolutionCost) in _schema.GetPossibleSchemas(mutationSelectionSet))
                {
                    possiblePlans.Enqueue(
                        node with
                        {
                            SchemaName = schemaName,
                            ResolutionCost = resolutionCost
                        });
                }
            }
            else
            {
                // A defer anchored inside a parent result (a list item, an object) is
                // planned as a keyed lookup rooted at the anchor's own type rather than a
                // fresh operation re-rooted at Query. Seeding a Lookup work item (instead
                // of a Root work item) lets PlanQueue.EnqueueLookupPlanNodes pick a direct
                // lookup for the anchor type when one exists; when none exists it falls
                // back to walking the path up to the root
                // (EnqueueParentPathLookupPlanNodes), which reproduces the previous root
                // re-fetch behavior.
                var anchorSet = new SelectionSet(
                    index.GetId(anchorSelectionSet),
                    anchorSelectionSet,
                    anchorType,
                    descriptor.Path);

                var backlog = Backlog.Empty.Push(
                    new OperationWorkItem(OperationWorkItemKind.Lookup, anchorSet)
                    {
                        AllowSourceSchemaReentry = true
                    });

                node = CreateIncrementalPlanNode(deferredOperation, index, backlog);
                possiblePlans.EnqueueBranches(node);
            }
        }

        if (possiblePlans.Count < 1)
        {
            possiblePlans.Enqueue(node);
        }

        var plan = Plan(operationId + "#defer_" + incrementalPlanId, possiblePlans, budget, cancellationToken);

        if (!plan.HasValue)
        {
            if (mutationAnchorType is not null)
            {
                // The mutation root fetch could not resolve the deferred fields through any
                // reachable subgraph: the anchor type has no lookup that lets the incremental
                // plan reach them without re-running the mutation root field. Fail clearly
                // instead of silently dropping the incremental plan.
                throw new DeferredMutationLookupRequiredException(descriptor.Path, mutationAnchorType.Name);
            }

            throw ThrowHelper.DeferredPlanNotFound(descriptor.Path);
        }

        return new DeferIncrementalPlanResult(
            plan.Value.Steps,
            plan.Value.InternalOperationDefinition);
    }

    /// <summary>
    /// Builds the initial <see cref="PlanNode"/> for a non-root-anchored incremental
    /// plan search seeded with <paramref name="backlog"/>.
    /// </summary>
    private PlanNode CreateIncrementalPlanNode(
        OperationDefinitionNode deferredOperation,
        ISelectionSetIndex index,
        Backlog backlog)
    {
        var remainingCost = PlannerCostEstimator.EstimateRemainingCost(
            _options,
            currentMaxDepth: 0,
#if NET10_0_OR_GREATER
            [],
#else
            ImmutableDictionary<int, int>.Empty,
#endif
            backlog.Cost);

        return new PlanNode
        {
            OperationDefinition = deferredOperation,
            InternalOperationDefinition = deferredOperation,
            ShortHash = "defer",
            SchemaName = Planning.PlanNode.UnresolvedSchemaName,
            Options = _options,
            SelectionSetIndex = index,
            Backlog = backlog,
            RemainingCost = remainingCost,
            OperationStepCount = 0
        };
    }

    /// <summary>
    /// Walks <paramref name="operation"/>'s selection set from its root type to locate
    /// the selection set at <paramref name="path"/> and resolves its GraphQL type. Used
    /// to find the anchor selection set for a non-root-anchored incremental plan, whose
    /// operation is a full path replica from <c>Query</c> down to the deferred fields.
    /// </summary>
    private bool TryLocateIncrementalPlanAnchor(
        OperationDefinitionNode operation,
        SelectionPath path,
        out SelectionSetNode anchorSelectionSet,
        out ITypeDefinition anchorType)
        => TryLocateSelectionSetAtPath(
            operation.SelectionSet,
            _schema.GetOperationType(operation.Operation),
            path,
            startIndex: 0,
            out anchorSelectionSet,
            out anchorType);

    /// <summary>
    /// Determines whether <paramref name="path"/> is exactly the root's own
    /// <c>node(id: ...)</c> field and, if so, locates that field within
    /// <paramref name="operation"/>.
    /// </summary>
    private bool TryLocateNodeFieldAnchor(
        OperationDefinitionNode operation,
        SelectionPath path,
        out FieldNode nodeField)
    {
        nodeField = null!;

        if (path.Length != 1 || path[0].Kind != SelectionPathSegmentKind.Field)
        {
            return false;
        }

        // A path segment carries the response name, so the AST field is resolved by
        // alias (or name when there is no alias) before its schema field is checked.
        var responseName = path[0].Name;

        foreach (var selection in operation.SelectionSet.Selections)
        {
            if (selection is FieldNode candidate
                && (candidate.Alias?.Value ?? candidate.Name.Value) == responseName)
            {
                if (_schema.QueryType.Fields.TryGetField(
                    candidate.Name.Value,
                    allowInaccessibleFields: true,
                    out var field)
                    && field is { Name: "node", Type: IInterfaceTypeDefinition { Name: "Node" } })
                {
                    nodeField = candidate;
                    return true;
                }

                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves requirements that can be satisfied by the enclosing plan scope out
    /// of the incremental plan and records them as parent-scope dependencies.
    /// </summary>
    /// <returns>
    /// The updated incremental plan step list, or the original list when no
    /// parent-scope requirements can be moved.
    /// </returns>
    internal ImmutableList<PlanStep> ApplyDeferRequirementsToParent(
        IncrementalPlanDescriptor descriptor,
        ImmutableList<PlanStep> incrementalPlanSteps,
        ParentPlanContext parentContext,
        PlanContextGraph contextGraph)
    {
        if (incrementalPlanSteps.Count < 2)
        {
            return incrementalPlanSteps;
        }

        // A producer step exists purely to supply another incremental-plan step's
        // requirement: it targets the defer's own anchor (the same entity the
        // enclosing scope already fetches down to) and every field it selects is
        // consumed as a requirement by its dependents, so it never carries
        // client-visible deferred output of its own. Such a step's data can always
        // be routed to the enclosing scope instead of being fetched a second time
        // inside this incremental plan. Earlier seeding always produced exactly one
        // such step at index 0; a lookup-anchored seed may produce it anywhere in
        // the list (or several, chained through each other), so every step is
        // considered.
        var anchorFieldNames = GetAnchorFieldNames(descriptor);
        var producers = FindDeferRequirementProducers(incrementalPlanSteps, descriptor.Path, anchorFieldNames);

        var producerIds = new HashSet<int>();
        foreach (var producer in producers)
        {
            producerIds.Add(producer.Id);
        }

        // Collect every step that depends directly on a producer; that is exactly
        // the set of steps whose requirements need to be routed to the enclosing
        // scope. A producer that targets an ancestor of the anchor only reaches the
        // entity the enclosing scope already fetches, so its own requirement is not
        // routed.
        var downstreamByStepId = new Dictionary<int, OperationPlanStep>();
        foreach (var producer in producers)
        {
            foreach (var dependentStepId in producer.Dependents)
            {
                if (incrementalPlanSteps.ById(dependentStepId) is OperationPlanStep dependentStep
                    && (!producerIds.Contains(dependentStepId) || dependentStep.Target.Equals(descriptor.Path)))
                {
                    downstreamByStepId[dependentStepId] = dependentStep;
                }
            }
        }

        // Route each requirement through a scope-walker: at each scope, try
        // same-subgraph inline first, then cross-subgraph promote; on both
        // failing, escalate to the next enclosing scope. The walker exits on
        // the first scope that successfully serves the requirement. At root
        // exhaustion, throw. Per-scope mutations (step-list + internal op)
        // are accumulated by scope identity and committed back to the graph
        // after the loop.
        var resolver = new ValueSelectionToSelectionSetRewriter(_schema);
        ScopeState? rootScopeState = null;
        var enclosingScopeStates = new Dictionary<IncrementalPlanDescriptor, ScopeState>();
        var lifted = new List<LiftedDeferRequirement>();
        var promotedIncrementalPlanStepIds = new HashSet<int>();
        var promotedParentSteps = new Dictionary<int, (ScopeState Scope, int ParentStepId)>();

        ScopeState ScopeStateFor(ParentPlanContext scope)
        {
            if (scope.OwnerDescriptor is { } owner)
            {
                if (!enclosingScopeStates.TryGetValue(owner, out var enclosingState))
                {
                    enclosingState = new ScopeState(scope.ParentSteps, scope.ParentInternalOperation);
                    enclosingScopeStates[owner] = enclosingState;
                }
                return enclosingState;
            }

            rootScopeState ??= new ScopeState(scope.ParentSteps, scope.ParentInternalOperation);
            return rootScopeState;
        }

        // A step that reads a requirement from a step remaining in this plan reads every
        // requirement from steps in this plan.
        var keptInPlanStepIds = new HashSet<int>();

        foreach (var (downstreamStepId, downstreamStep) in downstreamByStepId)
        {
            if (ReadsRequirementFromRemainingStep(incrementalPlanSteps, downstreamStep, producerIds)
                && TryProvideRequirementsInPlan(ref incrementalPlanSteps, downstreamStep, producerIds, resolver))
            {
                keptInPlanStepIds.Add(downstreamStepId);
                continue;
            }

            foreach (var (_, requirement) in downstreamStep.Requirements)
            {
                // A requirement must sit at or above the defer's own anchor: at the
                // anchor itself (the common case), or at an ancestor of it (a nested
                // defer bootstrapping a key at the enclosing defer's own anchor, several
                // path segments above its own, to reach its deeper one). A requirement
                // anchored below or beside the defer's own path would mean a producer
                // reaches somewhere this defer's own tree does not, which should not
                // happen given every producer's target is at or above this same path.
                if (!requirement.Path.IsParentOfOrSame(descriptor.Path))
                {
                    throw CreateUnsatisfiableDeferRequirementException(
                        producers[0],
                        requirement,
                        "requirement path lies outside the defer's anchor");
                }

                // See through a chain of producers that do nothing but relay this same
                // field from one subgraph to the next (a key value bootstrapped across
                // several subgraphs before the entity that actually needs the deferred
                // field is reached) to the step that truly grounds it, so both the
                // same-subgraph inline attempt and a cross-subgraph promotion target
                // the real origin instead of an intermediate pass-through hop.
                var (sourceStep, sourceRequirement) = ResolveDeferRequirementSource(
                    incrementalPlanSteps,
                    downstreamStep,
                    requirement,
                    producerIds);
                var schemaHint =
                    (sourceStep is not null
                        ? TryFindDeferRequirementProvider(incrementalPlanSteps, sourceStep, sourceRequirement)
                            ?.SchemaName
                        : null)
                    ?? producers[0].SchemaName!;

                var resolved = false;
                var walkScope = parentContext;

                while (walkScope is not null)
                {
                    var scopeState = ScopeStateFor(walkScope);

                    // Same-subgraph attempt: inline the requirement's field
                    // selection into an existing parent-scope step that
                    // already targets the path on the same source schema.
                    if (TryInlineDeferRequirementInScope(
                        sourceRequirement,
                        schemaHint,
                        resolver,
                        scopeState,
                        out var parentStepId,
                        out var isPartiallyResolvable))
                    {
                        lifted.Add(new LiftedDeferRequirement(requirement, downstreamStepId, parentStepId, scopeState));
                        resolved = true;
                        break;
                    }

                    // A partially hostable requirement lifts nothing, so the incremental plan
                    // stays self-contained.
                    if (isPartiallyResolvable)
                    {
                        return incrementalPlanSteps;
                    }

                    // When no existing parent-scope step can supply the
                    // requirement, add a dedicated step to the enclosing scope.
                    var (stepsAfterPromotion, newStepId, promotedIncrementalPlanStepId) =
                        PlanCrossSubgraphDeferRequirement(
                            sourceRequirement,
                            sourceStep ?? downstreamStep,
                            incrementalPlanSteps,
                            scopeState.Steps);

                    if (newStepId is { } resolvedStepId)
                    {
                        scopeState.Steps = stepsAfterPromotion;

                        if (stepsAfterPromotion.ById(resolvedStepId) is not OperationPlanStep promotedStep
                            || !TryLocateDeferRequirementTarget(
                                promotedStep,
                                sourceRequirement.Path,
                                out _,
                                out var promotedTargetType)
                            || !TryMirrorDeferRequirementIntoInternalOperation(
                                scopeState,
                                sourceRequirement.Path,
                                promotedTargetType,
                                resolver.Rewrite(sourceRequirement.Map, promotedTargetType)))
                        {
                            throw CreateUnsatisfiableDeferRequirementException(
                                producers[0],
                                requirement,
                                "a promoted step could not be mirrored into the enclosing operation");
                        }

                        lifted.Add(
                            new LiftedDeferRequirement(requirement, downstreamStepId, resolvedStepId, scopeState));

                        if (promotedIncrementalPlanStepId is { } pid)
                        {
                            promotedIncrementalPlanStepIds.Add(pid);
                            promotedParentSteps[pid] = (scopeState, resolvedStepId);
                        }

                        resolved = true;
                        break;
                    }

                    // This scope cannot serve the requirement. Escalate to
                    // the next enclosing scope and retry. At root exhaustion
                    // the loop exits with resolved=false.
                    walkScope = contextGraph.GetEnclosingScope(walkScope);
                }

                if (!resolved)
                {
                    // A requirement whose map names no single root field has no provider the
                    // scope walker can lift, so the incremental plan stays self-contained.
                    if ((requirement.InternalAlias ?? ExtractRootFieldName(requirement.Map.ToString())) is null)
                    {
                        return incrementalPlanSteps;
                    }

                    throw CreateUnsatisfiableDeferRequirementException(
                        producers[0],
                        requirement,
                        "no parent-reachable subgraph provides the required field");
                }
            }
        }

        // Every remaining step's requirement anchored exactly at the defer's own path is
        // retried against the parent scope; the edge and the now-unconsumed field are
        // cleaned up below once it succeeds. A step is rerouted only when every requirement it
        // reads from a step that stays in this plan can be rerouted.
        var rerouted = new List<(int ProviderStepId, int DownstreamStepId, string FieldName)>();
        var reroutedHeldFields = new Dictionary<(int ProviderStepId, string FieldName), OperationRequirement>();
        var liftedRequirementKeys = new HashSet<(int StepId, string Key)>();
        foreach (var entry in lifted)
        {
            liftedRequirementKeys.Add((entry.DownstreamStepId, entry.Requirement.Key));
        }

        foreach (var step in incrementalPlanSteps)
        {
            if (step is not OperationPlanStep downstreamStep
                || producerIds.Contains(downstreamStep.Id)
                || keptInPlanStepIds.Contains(downstreamStep.Id))
            {
                continue;
            }

            var scopeState = ScopeStateFor(parentContext);
            var stepsBeforeReroute = scopeState.Steps;
            var internalOperationBeforeReroute = scopeState.InternalOperation;
            var stepLifted = new List<LiftedDeferRequirement>();
            var stepRerouted = new List<(int ProviderStepId, int DownstreamStepId, string FieldName)>();
            var stepHeldFields = new List<(int ProviderStepId, OperationRequirement HeldField)>();
            var readsRemainingStep = false;

            foreach (var (_, requirement) in downstreamStep.Requirements)
            {
                // A requirement the pure-producer routing already lifted is read from the
                // parent scope whether or not it is rerouted here.
                var isLifted = liftedRequirementKeys.Contains((downstreamStep.Id, requirement.Key));

                if (TryFindDeferRequirementProvider(incrementalPlanSteps, downstreamStep, requirement)
                        is not { } providerStep
                    || producerIds.Contains(providerStep.Id))
                {
                    // No in-plan provider (already parent-sourced), or already
                    // handled by the pure-producer routing above.
                    continue;
                }

                // The provider must target the defer's own anchor or one of its ancestors,
                // matching FindDeferRequirementProducers' own restriction above.
                if (!requirement.Path.Equals(descriptor.Path)
                    || !providerStep.Target.IsParentOfOrSame(descriptor.Path))
                {
                    if (isLifted)
                    {
                        continue;
                    }

                    readsRemainingStep = true;
                    break;
                }

                var resolvedParentStepId = (int?)null;

                foreach (var candidateSchema in scopeState.Steps
                    .OfType<OperationPlanStep>()
                    .Select(candidate => candidate.SchemaName)
                    .Where(schemaName => schemaName is not null)
                    .Distinct(StringComparer.Ordinal))
                {
                    if (TryInlineDeferRequirementInScope(
                        requirement,
                        candidateSchema!,
                        resolver,
                        scopeState,
                        out var parentStepId,
                        out _))
                    {
                        resolvedParentStepId = parentStepId;
                        break;
                    }
                }

                if (resolvedParentStepId is not { } resolvedId)
                {
                    // The immediate parent scope cannot serve it either; fall back to
                    // the existing in-plan chaining onto providerStep.
                    if (isLifted)
                    {
                        continue;
                    }

                    readsRemainingStep = true;
                    break;
                }

                stepLifted.Add(new LiftedDeferRequirement(requirement, downstreamStep.Id, resolvedId, scopeState));

                var fieldName = requirement.InternalAlias ?? ExtractRootFieldName(requirement.Map.ToString());
                if (fieldName is not null)
                {
                    stepRerouted.Add((providerStep.Id, downstreamStep.Id, fieldName));

                    if (TryCreateReroutedHeldField(
                        descriptor,
                        anchorFieldNames,
                        providerStep,
                        fieldName,
                        scopeState,
                        resolvedId,
                        out var heldField))
                    {
                        stepHeldFields.Add((providerStep.Id, heldField));
                    }
                }
            }

            if (readsRemainingStep)
            {
                scopeState.Steps = stepsBeforeReroute;
                scopeState.InternalOperation = internalOperationBeforeReroute;
                continue;
            }

            lifted.AddRange(stepLifted);
            rerouted.AddRange(stepRerouted);

            foreach (var (providerStepId, heldField) in stepHeldFields)
            {
                reroutedHeldFields[(providerStepId, heldField.Key)] = heldField;
            }
        }

        if (lifted.Count == 0)
        {
            return incrementalPlanSteps;
        }

        // A promoted step runs in the enclosing scope, so the parent-scope step that supplies
        // one of its own requirements must feed it there.
        foreach (var entry in lifted)
        {
            if (!promotedParentSteps.TryGetValue(entry.DownstreamStepId, out var promoted))
            {
                continue;
            }

            if (!ReferenceEquals(promoted.Scope, entry.Scope))
            {
                throw CreateUnsatisfiableDeferRequirementException(
                    producers[0],
                    entry.Requirement,
                    "a promoted step and the provider of its requirement lie in different enclosing scopes");
            }

            AddParentStepDependent(entry.Scope, entry.ParentStepId, promoted.ParentStepId);
        }

        // Sever the in-plan edges that were rerouted to the parent scope and
        // drop the provider's field once nothing else consumes it.
        incrementalPlanSteps = RemoveReroutedInPlanEdges(
            incrementalPlanSteps,
            rerouted,
            descriptor.Path,
            anchorFieldNames,
            reroutedHeldFields);

        // Drop every producer step (its data now comes from the enclosing scope)
        // along with any incremental-plan step that got promoted wholesale into
        // the enclosing scope, and record parent-scope dependencies on the
        // remaining steps.
        var droppedStepIds = new HashSet<int>(promotedIncrementalPlanStepIds);
        foreach (var producer in producers)
        {
            droppedStepIds.Add(producer.Id);
        }

        // A step that fetches nothing the enclosing scope does not already hold at the anchor is dropped,
        // and the fields of the deferred group that it selected are delivered as held fields.
        var enclosingScope = ScopeStateFor(parentContext);
        bool dropped;

        do
        {
            dropped = false;

            foreach (var step in incrementalPlanSteps)
            {
                if (step is OperationPlanStep { SchemaName: not null } candidate
                    && !droppedStepIds.Contains(candidate.Id)
                    && droppedStepIds.Count + 1 < incrementalPlanSteps.Count
                    && droppedStepIds.IsSupersetOf(candidate.Dependents)
                    && IsRedundantStep(descriptor, candidate, anchorFieldNames, reroutedHeldFields, enclosingScope))
                {
                    droppedStepIds.Add(candidate.Id);
                    dropped = true;
                }
            }
        } while (dropped);

        var rewrittenIncrementalPlan = RewriteIncrementalPlanAfterDeferRequirementRouting(
            incrementalPlanSteps,
            lifted,
            droppedStepIds);

        // Record the parent-scope requirements on the descriptor. A step without a
        // parent dependency is served entirely by other steps in this incremental plan.
        foreach (var step in rewrittenIncrementalPlan)
        {
            if (step is not OperationPlanStep operationStep || operationStep.ParentDependencies.IsEmpty)
            {
                continue;
            }

            foreach (var (key, requirement) in operationStep.Requirements)
            {
                descriptor.Requirements.TryAdd(key, requirement);
            }
        }

        AddHeldClientFields(descriptor, anchorFieldNames, promotedParentSteps, rewrittenIncrementalPlan);

        foreach (var heldField in reroutedHeldFields.Values)
        {
            if (descriptor.Requirements.TryAdd(heldField.Key, heldField))
            {
                descriptor.HeldFields.Add(heldField);
            }
        }

        AddHeldEnclosingFields(descriptor, enclosingScope, rewrittenIncrementalPlan);

        // Publish scope updates before processing additional descriptors.
        if (rootScopeState is not null)
        {
            contextGraph.UpdateRootSteps(rootScopeState.Steps);
            contextGraph.UpdateRootInternalOperation(rootScopeState.InternalOperation);
        }
        foreach (var (ownerDescriptor, state) in enclosingScopeStates)
        {
            contextGraph.UpdateDeferContext(ownerDescriptor, state.Steps, state.InternalOperation);
        }

        return rewrittenIncrementalPlan;
    }

    /// <summary>
    /// Registers each leaf field of the deferred group that only a step promoted into the enclosing
    /// scope selects as a held field of the plan, so the plan delivers the field from the value the
    /// enclosing scope fetched.
    /// </summary>
    private void AddHeldClientFields(
        IncrementalPlanDescriptor descriptor,
        HashSet<string> anchorFieldNames,
        Dictionary<int, (ScopeState Scope, int ParentStepId)> promotedParentSteps,
        ImmutableList<PlanStep> remainingSteps)
    {
        foreach (var (scope, parentStepId) in promotedParentSteps.Values)
        {
            if (scope.Steps.ById(parentStepId) is not OperationPlanStep promotedStep
                || !TryLocateDeferRequirementTarget(
                    promotedStep,
                    descriptor.Path,
                    out var promotedSelectionSet,
                    out var promotedType)
                || promotedType is not FusionComplexTypeDefinition complexType)
            {
                continue;
            }

            foreach (var selection in promotedSelectionSet.Selections)
            {
                if (selection is not FieldNode { SelectionSet: null } field || field.Directives.Count > 0)
                {
                    continue;
                }

                var name = field.Alias?.Value ?? field.Name.Value;

                if (!anchorFieldNames.Contains(name)
                    || descriptor.Requirements.ContainsKey(name)
                    || !complexType.Fields.TryGetField(
                        field.Name.Value,
                        allowInaccessibleFields: true,
                        out var definition)
                    || remainingSteps.OfType<OperationPlanStep>().Any(
                        t => t.SchemaName is not null && SelectsFieldAtPath(t, descriptor.Path, name)))
                {
                    continue;
                }

                if (!TryLocateInternalSelectionSetAtPath(
                    scope.InternalOperation.SelectionSet,
                    descriptor.Path,
                    out var internalSelectionSet))
                {
                    continue;
                }

                if (!SelectsResponseName(internalSelectionSet, name)
                    && !TryMirrorDeferRequirementIntoInternalOperation(
                        scope,
                        descriptor.Path,
                        promotedType,
                        new SelectionSetNode([field])))
                {
                    continue;
                }

                var held = new OperationRequirement(
                    name,
                    definition.Type.ToTypeNode(),
                    descriptor.Path,
                    FieldSelectionMapParser.Parse(name));

                descriptor.Requirements.Add(name, held);
                descriptor.HeldFields.Add(held);
            }
        }
    }

    /// <summary>
    /// Creates the held field for the leaf field <paramref name="fieldName"/> of the deferred group that
    /// <paramref name="providerStep"/> selects and the step <paramref name="parentStepId"/> of the
    /// enclosing scope already fetches as a requirement.
    /// </summary>
    private bool TryCreateReroutedHeldField(
        IncrementalPlanDescriptor descriptor,
        HashSet<string> anchorFieldNames,
        OperationPlanStep providerStep,
        string fieldName,
        ScopeState scope,
        int parentStepId,
        [NotNullWhen(true)] out OperationRequirement? heldField)
    {
        heldField = null;

        if (!providerStep.Target.Equals(descriptor.Path)
            || !anchorFieldNames.Contains(fieldName)
            || descriptor.Requirements.ContainsKey(fieldName)
            || !IsPlainLeafSelection(GetStepEntitySelectionSet(providerStep), fieldName)
            || scope.Steps.ById(parentStepId) is not OperationPlanStep parentStep
            || !TryLocateDeferRequirementTarget(parentStep, descriptor.Path, out _, out var parentType)
            || parentType is not FusionComplexTypeDefinition complexType
            || !complexType.Fields.TryGetField(fieldName, allowInaccessibleFields: true, out var definition)
            || !TryLocateInternalSelectionSetAtPath(
                scope.InternalOperation.SelectionSet,
                descriptor.Path,
                out var internalSelectionSet)
            || !SelectsResponseName(internalSelectionSet, fieldName))
        {
            return false;
        }

        heldField = new OperationRequirement(
            fieldName,
            definition.Type.ToTypeNode(),
            descriptor.Path,
            FieldSelectionMapParser.Parse(fieldName));
        return true;
    }

    private static bool IsPlainLeafSelection(SelectionSetNode selectionSet, string fieldName)
    {
        foreach (var selection in selectionSet.Selections)
        {
            if (selection is FieldNode
                {
                    Alias: null,
                    SelectionSet: null,
                    Arguments.Count: 0,
                    Directives.Count: 0
                } field
                && field.Name.Value.Equals(fieldName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the enclosing scope unconditionally selects every field of the deferred group that
    /// <paramref name="step"/> selects at the anchor. <c>__typename</c> is added to the enclosing scope.
    /// </summary>
    private bool IsRedundantStep(
        IncrementalPlanDescriptor descriptor,
        OperationPlanStep step,
        HashSet<string> anchorFieldNames,
        Dictionary<(int ProviderStepId, string FieldName), OperationRequirement> reroutedHeldFields,
        ScopeState scope)
    {
        if (!step.Target.IsParentOfOrSame(descriptor.Path)
            || !TryLocateInternalSelectionSetAtPath(
                scope.InternalOperation.SelectionSet,
                descriptor.Path,
                out var enclosingSelectionSet))
        {
            return false;
        }

        // A step that targets an ancestor of the anchor is redundant only if it selects nothing beside the
        // path to the anchor.
        var anchorSelectionSet = GetStepEntitySelectionSet(step);

        for (var i = step.Target.Length; i < descriptor.Path.Length; i++)
        {
            var segment = descriptor.Path[i];

            if (segment.Kind is SelectionPathSegmentKind.Root)
            {
                continue;
            }

            if (segment.Kind is not SelectionPathSegmentKind.Field
                || anchorSelectionSet.Selections is not [FieldNode { SelectionSet: { } inner } pathField]
                || !(pathField.Alias?.Value ?? pathField.Name.Value).Equals(
                    segment.Name,
                    StringComparison.Ordinal))
            {
                return false;
            }

            anchorSelectionSet = inner;
        }

        var selectsTypeName = false;

        foreach (var selection in anchorSelectionSet.Selections)
        {
            if (selection is not FieldNode { SelectionSet: null } field)
            {
                return false;
            }

            var responseName = field.Alias?.Value ?? field.Name.Value;

            if (!anchorFieldNames.Contains(responseName)
                || reroutedHeldFields.ContainsKey((step.Id, responseName))
                || TryFindEnclosingCopy(enclosingSelectionSet, field, out _))
            {
                continue;
            }

            if (!field.Name.Value.Equals(IntrospectionFieldNames.TypeName, StringComparison.Ordinal))
            {
                return false;
            }

            selectsTypeName = true;
        }

        return !selectsTypeName || TryAddTypeNameToEnclosingScope(scope, descriptor.Path);
    }

    /// <summary>
    /// Registers each leaf field of the deferred group that no step of the plan selects and that the
    /// enclosing scope unconditionally selects at the anchor as a held field of the plan.
    /// </summary>
    private void AddHeldEnclosingFields(
        IncrementalPlanDescriptor descriptor,
        ScopeState scope,
        ImmutableList<PlanStep> remainingSteps)
    {
        if (!TryLocateIncrementalPlanAnchor(
                descriptor.Operation,
                descriptor.Path,
                out var anchorSelectionSet,
                out var anchorType)
            || anchorType is not FusionComplexTypeDefinition complexType
            || !TryLocateInternalSelectionSetAtPath(
                scope.InternalOperation.SelectionSet,
                descriptor.Path,
                out var enclosingSelectionSet))
        {
            return;
        }

        foreach (var selection in anchorSelectionSet.Selections)
        {
            if (selection is not FieldNode { SelectionSet: null } field)
            {
                continue;
            }

            var responseName = field.Alias?.Value ?? field.Name.Value;

            if (descriptor.Requirements.ContainsKey(responseName)
                || !TryFindEnclosingCopy(enclosingSelectionSet, field, out var enclosingResponseName)
                || remainingSteps.OfType<OperationPlanStep>().Any(
                    t => t.SchemaName is not null && SelectsFieldAtPath(t, descriptor.Path, responseName)))
            {
                continue;
            }

            ITypeNode type;

            if (field.Name.Value.Equals(IntrospectionFieldNames.TypeName, StringComparison.Ordinal))
            {
                type = new NonNullTypeNode(new NamedTypeNode("String"));
            }
            else if (complexType.Fields.TryGetField(
                field.Name.Value,
                allowInaccessibleFields: true,
                out var definition))
            {
                type = definition.Type.ToTypeNode();
            }
            else
            {
                continue;
            }

            var held = new OperationRequirement(
                responseName,
                type,
                descriptor.Path,
                FieldSelectionMapParser.Parse(enclosingResponseName));

            descriptor.Requirements.Add(responseName, held);
            descriptor.HeldFields.Add(held);
        }
    }

    private static bool TryFindEnclosingCopy(
        SelectionSetNode enclosingSelectionSet,
        FieldNode field,
        [NotNullWhen(true)] out string? responseName)
    {
        foreach (var selection in enclosingSelectionSet.Selections)
        {
            if (selection is FieldNode { SelectionSet: null } candidate
                && candidate.Name.Value.Equals(field.Name.Value, StringComparison.Ordinal)
                && IsUnconditionallySelected(candidate)
                && HasSameArguments(candidate, field))
            {
                responseName = candidate.Alias?.Value ?? candidate.Name.Value;
                return true;
            }
        }

        responseName = null;
        return false;
    }

    private static bool HasSameArguments(FieldNode first, FieldNode second)
    {
        if (first.Arguments.Count != second.Arguments.Count)
        {
            return false;
        }

        foreach (var argument in first.Arguments)
        {
            var found = false;

            foreach (var other in second.Arguments)
            {
                if (other.Name.Value.Equals(argument.Name.Value, StringComparison.Ordinal))
                {
                    found = SyntaxComparer.BySyntax.Equals(argument.Value, other.Value);
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

    /// <summary>
    /// Adds <c>__typename</c> at <paramref name="path"/> to a step of <paramref name="scope"/> that
    /// unconditionally fetches the object at that path, and to the internal operation of the scope.
    /// </summary>
    private bool TryAddTypeNameToEnclosingScope(ScopeState scope, SelectionPath path)
    {
        for (var i = 0; i < scope.Steps.Count; i++)
        {
            if (scope.Steps[i] is not OperationPlanStep { SchemaName: not null, Conditions.Length: 0 } parentStep
                || !parentStep.Target.IsParentOfOrSame(path)
                || !TryLocateDeferRequirementTarget(parentStep, path, out var selectionSet, out var targetType))
            {
                continue;
            }

            var typeName = new FieldNode(IntrospectionFieldNames.TypeName);
            var typeNameSelection = new SelectionSetNode([typeName]);

            if (!TryMirrorDeferRequirementIntoInternalOperation(scope, path, targetType, typeNameSelection))
            {
                return false;
            }

            if (!TryFindEnclosingCopy(selectionSet, typeName, out _))
            {
                var updatedSelectionSet = new SelectionSetNode([.. selectionSet.Selections, typeName]);
                var updatedStep = WithEntitySelectionSet(
                    parentStep,
                    ReplaceSelectionSetAtPath(
                        GetStepEntitySelectionSet(parentStep),
                        path,
                        parentStep.Target.Length,
                        updatedSelectionSet));

                scope.Steps = scope.Steps.SetItem(i, updatedStep);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects an incremental plan whose entire deferred output is already available from
    /// the enclosing plan scope, either as the anchor type's own key fields or as fields
    /// already selected there for an unrelated reason, and if so makes those fields visible
    /// on the enclosing step instead of producing an incremental plan for
    /// <paramref name="descriptor"/>.
    /// </summary>
    private bool TryAbsorbFullyRedundantDefer(
        IncrementalPlanDescriptor descriptor,
        ImmutableList<PlanStep> rewrittenIncrementalPlan,
        ParentPlanContext parentContext,
        PlanContextGraph contextGraph)
    {
        if (descriptor.Path.IsRoot || descriptor.Operation.Operation != OperationType.Query)
        {
            return false;
        }

        // A held field is absorbed with the plan only if the enclosing scope selects it under the same name.
        foreach (var heldField in descriptor.HeldFields)
        {
            if (!heldField.Map.ToString().Equals(heldField.Key, StringComparison.Ordinal))
            {
                return false;
            }
        }

        // Restricted to a single surviving producer: multiple concurrent client-visible
        // steps split across schemas are outside the scope of this optimization.
        OperationPlanStep? onlyStep = null;

        foreach (var step in rewrittenIncrementalPlan)
        {
            if (step is not OperationPlanStep operationStep || onlyStep is not null)
            {
                return false;
            }

            onlyStep = operationStep;
        }

        if (onlyStep is null || onlyStep.SchemaName is null)
        {
            return false;
        }

        // The parent step only needs to reach as far as the anchor path; its own
        // Definition may nest further beyond Target (for example a plain root fetch
        // whose Target is the operation root but whose Definition still walks all the
        // way down to the anchor). A conditionally executed step (guarded by @skip or
        // @include, for example the eager copy a variable-conditioned @defer keeps in
        // the main operation) is excluded: its data is not unconditionally available.
        // Prefer a parent step on the same source schema as the surviving incremental step
        // first: when several parent steps reach the anchor path across different schemas,
        // that is the one whose selection set the fieldNames membership check below actually
        // needs to inspect. Only when no such step exists (the surviving step's schema never
        // appears in the parent scope at all) does path/conditions alone decide, so a defer
        // whose data collapses onto a schema the parent never separately visits can still be
        // absorbed against whatever schema does cover the anchor path there.
        OperationPlanStep? parentStep = null;
        OperationPlanStep? fallbackParentStep = null;

        foreach (var step in parentContext.ParentSteps)
        {
            if (step is not OperationPlanStep candidate
                || !candidate.Target.IsParentOfOrSame(descriptor.Path)
                || candidate.Conditions.Length != 0)
            {
                continue;
            }

            if (string.Equals(candidate.SchemaName, onlyStep.SchemaName, StringComparison.Ordinal))
            {
                parentStep = candidate;
                break;
            }

            fallbackParentStep ??= candidate;
        }

        parentStep ??= fallbackParentStep;

        if (parentStep is null)
        {
            return false;
        }

        if (!TryLocateSelectionSetAtPath(
            GetStepEntitySelectionSet(onlyStep),
            GetStepEntityType(onlyStep),
            descriptor.Path,
            onlyStep.Target.Length,
            out var deferredFieldsSelectionSet,
            out _))
        {
            return false;
        }

        if (!TryCollectFlatFieldNames(deferredFieldsSelectionSet, out var fieldNames) || fieldNames.Count == 0)
        {
            return false;
        }

        if (!TryLocateSelectionSetAtPath(
            GetStepEntitySelectionSet(parentStep),
            GetStepEntityType(parentStep),
            descriptor.Path,
            parentStep.Target.Length,
            out var parentTargetSelectionSet,
            out _))
        {
            return false;
        }

        var alreadyPresent = CollectPlainFieldNames(parentTargetSelectionSet);

        var allAlreadyPresent = true;
        foreach (var fieldName in fieldNames)
        {
            // Every schema resolves __typename for free, so it is never the field that
            // decides whether the parent already covers this defer's output.
            if (fieldName.Equals("__typename", StringComparison.Ordinal))
            {
                continue;
            }

            if (!alreadyPresent.Contains(fieldName))
            {
                allAlreadyPresent = false;
                break;
            }
        }

        if (!allAlreadyPresent)
        {
            // A genuine keyed lookup whose output is not already available at the parent
            // is a real deferral, not redundant work.
            if (onlyStep.Lookup is not null)
            {
                return false;
            }

            // The only remaining case is a redundant re-fetch of the parent's own path on
            // the same source schema (no lookup involved): every field missing from the
            // parent must be a key field of the anchor type, which that schema resolves
            // for free alongside whatever it already fetches there. A different producing
            // schema gives no such guarantee, so schema identity is required here even
            // though the parent-step search above no longer requires it up front.
            if (!string.Equals(parentStep.SchemaName, onlyStep.SchemaName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryLocateIncrementalPlanAnchor(descriptor.Operation, descriptor.Path, out _, out var anchorType))
            {
                return false;
            }

            var keyFieldNames = GetEntityKeyFieldNames(anchorType);

            foreach (var fieldName in fieldNames)
            {
                if (fieldName.Equals("__typename", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!alreadyPresent.Contains(fieldName) && !keyFieldNames.Contains(fieldName))
                {
                    return false;
                }
            }
        }

        var updatedTargetSelectionSet = MakeFieldsVisible(parentTargetSelectionSet, fieldNames);
        var updatedParentStep = ReferenceEquals(updatedTargetSelectionSet, parentTargetSelectionSet)
            ? parentStep
            : WithEntitySelectionSet(
                parentStep,
                ReplaceSelectionSetAtPath(
                    GetStepEntitySelectionSet(parentStep),
                    descriptor.Path,
                    parentStep.Target.Length,
                    updatedTargetSelectionSet));

        var updatedParentSteps = ReferenceEquals(updatedParentStep, parentStep)
            ? parentContext.ParentSteps
            : parentContext.ParentSteps.SetItem(
                parentContext.ParentSteps.IndexOf(parentStep),
                updatedParentStep);

        var updatedInternalOperation = parentContext.ParentInternalOperation;

        if (TryLocateInternalSelectionSetAtPath(
            updatedInternalOperation.SelectionSet, descriptor.Path, out var internalTargetSelectionSet))
        {
            foreach (var heldField in descriptor.HeldFields)
            {
                fieldNames.Add(heldField.Key);
            }

            var updatedInternalTargetSelectionSet = MakeFieldsVisible(internalTargetSelectionSet, fieldNames);

            if (!ReferenceEquals(updatedInternalTargetSelectionSet, internalTargetSelectionSet))
            {
                updatedInternalOperation = ReplaceInternalSelectionSetAtPath(
                    updatedInternalOperation,
                    descriptor.Path,
                    updatedInternalTargetSelectionSet);
            }
        }

        if (parentContext.Kind == ParentScope.Root)
        {
            contextGraph.UpdateRootSteps(updatedParentSteps);
            contextGraph.UpdateRootInternalOperation(updatedInternalOperation);
        }
        else
        {
            contextGraph.UpdateDeferContext(
                parentContext.OwnerDescriptor!, updatedParentSteps, updatedInternalOperation);
        }

        return true;
    }

    /// <summary>
    /// Collects the response names of every top-level field selected in
    /// <paramref name="selectionSet"/>, ignoring aliases. Only flat, unaliased field
    /// selections are supported by <see cref="TryAbsorbFullyRedundantDefer"/>; anything
    /// else makes the caller decline the optimization. The synthetic <c>fusion__empty</c>
    /// placeholder is skipped, since it carries no client-visible meaning, but a
    /// client-selected plain <c>__typename</c> is collected like any other field. A field
    /// carrying any other directive (an <c>@skip</c>/<c>@include</c> guard, for example) is
    /// not unconditionally selected, so it makes the caller decline instead.
    /// </summary>
    private static bool TryCollectFlatFieldNames(SelectionSetNode selectionSet, out List<string> fieldNames)
    {
        fieldNames = new List<string>(selectionSet.Selections.Count);

        foreach (var selection in selectionSet.Selections)
        {
            if (selection is not FieldNode field || field.SelectionSet is not null || field.Alias is not null)
            {
                fieldNames = [];
                return false;
            }

            if (field.Name.Value.Equals("__typename", StringComparison.Ordinal)
                && field.Directives.Count == 1
                && field.Directives[0].Name.Value.Equals("fusion__empty", StringComparison.Ordinal))
            {
                continue;
            }

            if (field.Directives.Count != 0)
            {
                fieldNames = [];
                return false;
            }

            fieldNames.Add(field.Name.Value);
        }

        return true;
    }

    /// <summary>
    /// Collects the names of every unaliased, unconditionally selected top-level field in
    /// <paramref name="selectionSet"/>, marked with <c>fusion__requirement</c> or not.
    /// </summary>
    private static HashSet<string> CollectPlainFieldNames(SelectionSetNode selectionSet)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var selection in selectionSet.Selections)
        {
            if (selection is FieldNode { Alias: null } field && IsUnconditionallySelected(field))
            {
                names.Add(field.Name.Value);
            }
        }

        return names;
    }

    /// <summary>
    /// Determines whether <paramref name="field"/> carries no directives, or only the
    /// internal <c>fusion__requirement</c> marker, meaning it is fetched unconditionally
    /// rather than guarded by <c>@skip</c>/<c>@include</c>.
    /// </summary>
    private static bool IsUnconditionallySelected(FieldNode field)
        => field.Directives.Count == 0
            || (field.Directives.Count == 1
                && field.Directives[0].Name.Value.Equals("fusion__requirement", StringComparison.Ordinal));

    /// <summary>
    /// Collects the field names every possible lookup for <paramref name="anchorType"/>
    /// requires as input.
    /// </summary>
    private ImmutableHashSet<string> GetEntityKeyFieldNames(ITypeDefinition anchorType)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var lookup in _schema.GetPossibleLookups(anchorType))
        {
            foreach (var selection in lookup.Requirements.Selections)
            {
                if (selection is FieldNode field)
                {
                    builder.Add(field.Name.Value);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns a copy of <paramref name="selectionSet"/> where every field named in
    /// <paramref name="fieldNames"/> is a plain, client-visible selection: an existing
    /// unaliased field has its <c>fusion__requirement</c> or <c>fusion__empty</c> marker
    /// removed, and a field not yet selected is added.
    /// </summary>
    private static SelectionSetNode MakeFieldsVisible(SelectionSetNode selectionSet, List<string> fieldNames)
    {
        var selections = selectionSet.Selections;

        var newSelections = new List<ISelectionNode>(selections.Count + fieldNames.Count);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;

        foreach (var selection in selections)
        {
            if (selection is FieldNode { Alias: null } field)
            {
                present.Add(field.Name.Value);

                if (fieldNames.Contains(field.Name.Value)
                    && (TryRemoveRequirementDirective(field.Directives, out var strippedDirectives)
                        || TryRemoveEmptyPlaceholderDirective(field.Directives, out strippedDirectives)))
                {
                    newSelections.Add(field.WithDirectives(strippedDirectives));
                    changed = true;
                    continue;
                }
            }

            newSelections.Add(selection);
        }

        foreach (var fieldName in fieldNames)
        {
            if (present.Add(fieldName))
            {
                newSelections.Add(new FieldNode(fieldName));
                changed = true;
            }
        }

        return changed ? new SelectionSetNode(newSelections) : selectionSet;
    }

    /// <summary>
    /// Attempts to strip the synthetic <c>fusion__empty</c> placeholder marker from
    /// <paramref name="directives"/>, used by <see cref="MakeFieldsVisible"/> to upgrade an
    /// existing placeholder <c>__typename</c> selection (kept only to satisfy an
    /// otherwise-empty selection set) into a plain, client-visible field.
    /// </summary>
    private static bool TryRemoveEmptyPlaceholderDirective(
        IReadOnlyList<DirectiveNode> directives,
        out IReadOnlyList<DirectiveNode> result)
    {
        for (var i = 0; i < directives.Count; i++)
        {
            if (directives[i].Name.Value.Equals("fusion__empty", StringComparison.Ordinal))
            {
                var remaining = new List<DirectiveNode>(directives.Count - 1);

                for (var j = 0; j < directives.Count; j++)
                {
                    if (!directives[j].Name.Value.Equals("fusion__empty", StringComparison.Ordinal))
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
    /// Replaces the selection set at <paramref name="path"/> within <paramref name="operation"/>
    /// with <paramref name="replacement"/>.
    /// </summary>
    private static OperationDefinitionNode ReplaceInternalSelectionSetAtPath(
        OperationDefinitionNode operation,
        SelectionPath path,
        SelectionSetNode replacement)
        => operation.WithSelectionSet(ReplaceSelectionSetAtPath(operation.SelectionSet, path, 0, replacement));

    private static SelectionSetNode ReplaceSelectionSetAtPath(
        SelectionSetNode current,
        SelectionPath path,
        int index,
        SelectionSetNode replacement)
    {
        if (index == path.Length)
        {
            return replacement;
        }

        var segment = path[index];
        var selections = new List<ISelectionNode>(current.Selections.Count);
        var replaced = false;

        foreach (var selection in current.Selections)
        {
            if (!replaced
                && segment.Kind == SelectionPathSegmentKind.Field
                && selection is FieldNode { SelectionSet: { } fieldSelectionSet } field
                && (field.Alias?.Value == segment.Name || field.Name.Value == segment.Name))
            {
                selections.Add(
                    field.WithSelectionSet(
                        ReplaceSelectionSetAtPath(fieldSelectionSet, path, index + 1, replacement)));
                replaced = true;
                continue;
            }

            if (!replaced
                && segment.Kind == SelectionPathSegmentKind.InlineFragment
                && selection is InlineFragmentNode fragment
                && fragment.TypeCondition?.Name.Value == segment.Name)
            {
                selections.Add(
                    fragment.WithSelectionSet(
                        ReplaceSelectionSetAtPath(fragment.SelectionSet, path, index + 1, replacement)));
                replaced = true;
                continue;
            }

            selections.Add(selection);
        }

        return replaced ? new SelectionSetNode(selections) : current;
    }

    /// <summary>
    /// Tries to inline the field selection implied by a defer's
    /// <paramref name="requirement"/> into a parent-scope step that already
    /// targets the requirement's path on the same source schema. Uses the
    /// planner's same helper as intra-plan field-requirement inlining so the
    /// operation document rewrite and selection-set index bookkeeping match.
    /// Mirrors the inline onto the parent's internal operation so the
    /// compiled parent Operation carries the field (the runtime composite
    /// result document relies on that field being preserved during result
    /// merging). Nothing is injected and <paramref name="isPartiallyResolvable"/> is set
    /// when the step can host only part of the requirement.
    /// </summary>
    private bool TryInlineDeferRequirementInScope(
        OperationRequirement requirement,
        string schemaName,
        ValueSelectionToSelectionSetRewriter resolver,
        ScopeState scopeState,
        out int parentStepId,
        out bool isPartiallyResolvable)
    {
        parentStepId = 0;
        isPartiallyResolvable = false;

        for (var i = 0; i < scopeState.Steps.Count; i++)
        {
            if (scopeState.Steps[i] is not OperationPlanStep parentStep)
            {
                continue;
            }

            if (!string.Equals(parentStep.SchemaName, schemaName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!parentStep.Target.IsParentOfOrSame(requirement.Path))
            {
                continue;
            }

            if (!TryAddDeferRequirementToStep(
                parentStep,
                requirement,
                resolver,
                out var updatedParentStep,
                out var targetType,
                out var injectionSelections,
                out isPartiallyResolvable))
            {
                if (isPartiallyResolvable)
                {
                    return false;
                }

                continue;
            }

            scopeState.Steps = scopeState.Steps.SetItem(i, updatedParentStep);

            TryMirrorDeferRequirementIntoInternalOperation(
                scopeState,
                requirement.Path,
                targetType,
                injectionSelections);

            parentStepId = parentStep.Id;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Adds the field selection of <paramref name="requirement"/> to <paramref name="step"/> at the
    /// requirement's path. Nothing is added and <paramref name="isPartiallyResolvable"/> is set when
    /// the step can host only part of the requirement.
    /// </summary>
    private bool TryAddDeferRequirementToStep(
        OperationPlanStep step,
        OperationRequirement requirement,
        ValueSelectionToSelectionSetRewriter resolver,
        out OperationPlanStep updatedStep,
        out ITypeDefinition targetType,
        out SelectionSetNode injectionSelections,
        out bool isPartiallyResolvable)
    {
        updatedStep = step;
        injectionSelections = null!;
        isPartiallyResolvable = false;

        if (!TryLocateDeferRequirementTarget(
            step,
            requirement.Path,
            out _,
            out targetType))
        {
            return false;
        }

        try
        {
            injectionSelections = resolver.Rewrite(requirement.Map, targetType);
        }
        catch
        {
            return false;
        }

        var stepIndex = SelectionSetIndexer.Create(step.Definition).ToBuilder();
        var targetId = stepIndex.GetId(
            LocateSelectionSetAtPath(
                GetStepEntitySelectionSet(step),
                requirement.Path,
                step.Target.Length));

        RegisterRequirementSelectionSets(injectionSelections, stepIndex);

        if (!TryInlineSelectionSetIntoStep(
            step,
            targetId,
            targetType,
            requirement.Path,
            injectionSelections,
            dependentStepId: 0,
            stepIndex,
            new RequirementAliasContext([], RequirementAliasRegistry.Empty),
            out var inlinedStep,
            out var unresolvable,
            out var fieldsWithRequirements))
        {
            return false;
        }

        if (!unresolvable.IsEmpty || !fieldsWithRequirements.IsEmpty)
        {
            isPartiallyResolvable = true;
            return false;
        }

        updatedStep = inlinedStep with
        {
            Dependents = step.Dependents,
            SelectionSets = SelectionSetIndexer.CreateIdSet(inlinedStep.Definition.SelectionSet, stepIndex)
        };
        return true;
    }

    private static void AddParentStepDependent(ScopeState scopeState, int providerStepId, int dependentStepId)
    {
        for (var i = 0; i < scopeState.Steps.Count; i++)
        {
            if (scopeState.Steps[i] is OperationPlanStep provider && provider.Id == providerStepId)
            {
                scopeState.Steps = scopeState.Steps.SetItem(
                    i,
                    provider with { Dependents = provider.Dependents.Add(dependentStepId) });
                return;
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="selections"/> to the enclosing scope's internal operation at
    /// <paramref name="path"/> so the compiled Operation (and the runtime composite result
    /// document bound to it) carries the fields a parent-scope step supplies to a deferred step.
    /// </summary>
    private bool TryMirrorDeferRequirementIntoInternalOperation(
        ScopeState scopeState,
        SelectionPath path,
        ITypeDefinition targetType,
        SelectionSetNode selections)
    {
        if (!TryLocateInternalSelectionSetAtPath(
            scopeState.InternalOperation.SelectionSet,
            path,
            out var internalTargetSelectionSet))
        {
            return false;
        }

        var overallIndex = SelectionSetIndexer.Create(scopeState.InternalOperation).ToBuilder();
        var overallTargetId = overallIndex.GetId(internalTargetSelectionSet);
        scopeState.InternalOperation = InlineSelectionsIntoOverallOperation(
            scopeState.InternalOperation,
            overallIndex,
            targetType,
            overallTargetId,
            selections);
        return true;
    }

    /// <summary>
    /// Mutable planning state for an enclosing scope while parent-scope
    /// requirements are resolved.
    /// </summary>
    private sealed class ScopeState(
        ImmutableList<PlanStep> steps,
        OperationDefinitionNode internalOperation)
    {
        public ImmutableList<PlanStep> Steps { get; set; } = steps;

        public OperationDefinitionNode InternalOperation { get; set; } = internalOperation;
    }

    private static bool TryLocateInternalSelectionSetAtPath(
        SelectionSetNode root,
        SelectionPath path,
        out SelectionSetNode targetSelectionSet)
    {
        var currentSet = root;

        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            switch (segment.Kind)
            {
                case SelectionPathSegmentKind.Field:
                    FieldNode? fieldNode = null;
                    foreach (var selection in currentSet.Selections)
                    {
                        if (selection is FieldNode candidate
                            && (candidate.Alias?.Value == segment.Name || candidate.Name.Value == segment.Name))
                        {
                            fieldNode = candidate;
                            break;
                        }
                    }

                    if (fieldNode?.SelectionSet is null)
                    {
                        targetSelectionSet = null!;
                        return false;
                    }

                    currentSet = fieldNode.SelectionSet;
                    break;

                case SelectionPathSegmentKind.InlineFragment:
                    InlineFragmentNode? fragment = null;
                    foreach (var selection in currentSet.Selections)
                    {
                        if (selection is InlineFragmentNode candidate
                            && candidate.TypeCondition?.Name.Value == segment.Name)
                        {
                            fragment = candidate;
                            break;
                        }
                    }

                    if (fragment is null)
                    {
                        targetSelectionSet = null!;
                        return false;
                    }

                    currentSet = fragment.SelectionSet;
                    break;

                default:
                    targetSelectionSet = null!;
                    return false;
            }
        }

        targetSelectionSet = currentSet;
        return true;
    }

    /// <summary>
    /// Creates a parent-scope step for a requirement that cannot be satisfied
    /// by an existing parent-scope step.
    /// </summary>
    private (ImmutableList<PlanStep> UpdatedParentSteps, int? NewStepId, int? PromotedIncrementalPlanStepId)
        PlanCrossSubgraphDeferRequirement(
            OperationRequirement requirement,
            OperationPlanStep consumingStep,
            ImmutableList<PlanStep> incrementalPlanSteps,
            ImmutableList<PlanStep> parentSteps)
    {
        if (TryFindDeferRequirementProvider(incrementalPlanSteps, consumingStep, requirement) is not { } providerStep)
        {
            return (parentSteps, null, null);
        }

        if (providerStep.SchemaName is null)
        {
            return (parentSteps, null, null);
        }

        var index = SelectionSetIndexer.Create(providerStep.Definition);
        var newParentStepId = parentSteps.NextId();
        var promotedStep = providerStep with
        {
            Id = newParentStepId,
            Dependents = [],
            ParentDependencies = [],
            SelectionSets = SelectionSetIndexer.CreateIdSet(providerStep.Definition.SelectionSet, index),
            RootSelectionSetId = index.GetId(providerStep.Definition.SelectionSet)
        };

        return (parentSteps.Add(promotedStep), newParentStepId, providerStep.Id);
    }

    /// <summary>
    /// Determines whether one of <paramref name="step"/>'s requirements is provided by a step
    /// that is not in <paramref name="producerIds"/>.
    /// </summary>
    private bool ReadsRequirementFromRemainingStep(
        ImmutableList<PlanStep> incrementalPlanSteps,
        OperationPlanStep step,
        HashSet<int> producerIds)
    {
        foreach (var (_, requirement) in step.Requirements)
        {
            if (TryFindDeferRequirementProvider(incrementalPlanSteps, step, requirement) is not null
                && !IsProvidedByProducer(incrementalPlanSteps, step, requirement, producerIds))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Makes a step that <paramref name="step"/> depends on and that is not in
    /// <paramref name="producerIds"/> select each requirement of <paramref name="step"/> that a
    /// step in <paramref name="producerIds"/> provides. <paramref name="incrementalPlanSteps"/> is
    /// unchanged when one cannot be provided.
    /// </summary>
    private bool TryProvideRequirementsInPlan(
        ref ImmutableList<PlanStep> incrementalPlanSteps,
        OperationPlanStep step,
        HashSet<int> producerIds,
        ValueSelectionToSelectionSetRewriter resolver)
    {
        var steps = incrementalPlanSteps;

        foreach (var (_, requirement) in step.Requirements)
        {
            if (!IsProvidedByProducer(steps, step, requirement, producerIds))
            {
                continue;
            }

            // The value of an aliased requirement is read under its alias.
            if (requirement.InternalAlias is not null
                || ExtractRootFieldName(requirement.Map.ToString()) is not { } fieldName)
            {
                return false;
            }

            var provided = false;

            // A step that already selects the field is preferred over one that is extended with it.
            for (var pass = 0; pass < 2 && !provided; pass++)
            {
                for (var i = 0; i < steps.Count; i++)
                {
                    if (steps[i] is not OperationPlanStep { SchemaName: not null } candidate
                        || producerIds.Contains(candidate.Id)
                        || !candidate.Dependents.Contains(step.Id)
                        || !candidate.Target.IsParentOfOrSame(requirement.Path))
                    {
                        continue;
                    }

                    if (pass == 0)
                    {
                        if (SelectsFieldAtPath(candidate, requirement.Path, fieldName))
                        {
                            provided = true;
                            break;
                        }

                        continue;
                    }

                    if (TryAddDeferRequirementToStep(
                        candidate,
                        requirement,
                        resolver,
                        out var updatedCandidate,
                        out _,
                        out _,
                        out _))
                    {
                        steps = steps.SetItem(i, updatedCandidate);
                        provided = true;
                        break;
                    }
                }
            }

            if (!provided)
            {
                return false;
            }
        }

        incrementalPlanSteps = steps;
        return true;
    }

    /// <summary>
    /// Determines whether a step in <paramref name="producerIds"/> that <paramref name="step"/>
    /// depends on selects the field of <paramref name="requirement"/> at the requirement's path.
    /// </summary>
    private bool IsProvidedByProducer(
        ImmutableList<PlanStep> incrementalPlanSteps,
        OperationPlanStep step,
        OperationRequirement requirement,
        HashSet<int> producerIds)
    {
        var fieldName = requirement.InternalAlias ?? ExtractRootFieldName(requirement.Map.ToString());

        if (fieldName is null)
        {
            return false;
        }

        foreach (var producerId in producerIds)
        {
            if (incrementalPlanSteps.ById(producerId) is OperationPlanStep producer
                && producer.Dependents.Contains(step.Id)
                && producer.Target.IsParentOfOrSame(requirement.Path)
                && SelectsFieldAtPath(producer, requirement.Path, fieldName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="step"/> selects a field with the response name
    /// <paramref name="fieldName"/> on the object at <paramref name="path"/>.
    /// </summary>
    private bool SelectsFieldAtPath(OperationPlanStep step, SelectionPath path, string fieldName)
        => TryLocateDeferRequirementTarget(step, path, out var selectionSet, out _)
            && SelectsResponseName(selectionSet, fieldName);

    /// <summary>
    /// Determines whether <paramref name="selectionSet"/> selects a field with the response name
    /// <paramref name="responseName"/>, descending through inline fragments.
    /// </summary>
    private static bool SelectsResponseName(SelectionSetNode selectionSet, string responseName)
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field
                    when (field.Alias?.Value ?? field.Name.Value).Equals(responseName, StringComparison.Ordinal):
                    return true;

                case InlineFragmentNode fragment when SelectsResponseName(fragment.SelectionSet, responseName):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks past any in-plan producer that does nothing but relay the same field under
    /// a different schema, returning the step and requirement that first ground it.
    /// </summary>
    private static (OperationPlanStep? ConsumingStep, OperationRequirement Requirement) ResolveDeferRequirementSource(
        ImmutableList<PlanStep> incrementalPlanSteps,
        OperationPlanStep downstreamStep,
        OperationRequirement requirement,
        HashSet<int> producerIds)
    {
        var current = downstreamStep;
        var currentRequirement = requirement;
        var visited = new HashSet<int>();

        while (true)
        {
            if (TryFindDeferRequirementProvider(incrementalPlanSteps, current, currentRequirement)
                is not { } provider)
            {
                return (null, currentRequirement);
            }

            if (!producerIds.Contains(provider.Id)
                || provider.Requirements.Count != 1
                || !visited.Add(provider.Id))
            {
                return (current, currentRequirement);
            }

            var neededFieldName =
                currentRequirement.InternalAlias ?? ExtractRootFieldName(currentRequirement.Map.ToString());
            var upstreamRequirement = provider.Requirements.Values.Single();
            var upstreamFieldName =
                upstreamRequirement.InternalAlias ?? ExtractRootFieldName(upstreamRequirement.Map.ToString());

            if (neededFieldName is null
                || upstreamFieldName is null
                || !neededFieldName.Equals(upstreamFieldName, StringComparison.Ordinal)
                || !upstreamRequirement.Path.Equals(currentRequirement.Path))
            {
                return (current, currentRequirement);
            }

            current = provider;
            currentRequirement = upstreamRequirement;
        }
    }

    /// <summary>
    /// Locates the incremental plan step that produces <paramref name="requirement"/>'s
    /// value for <paramref name="consumingStep"/>. The provider is a step the
    /// consuming step depends on whose target is a parent-of-or-same ancestor
    /// of the requirement path (so the provider's tree reaches the required
    /// entity) and whose selection set contains the requirement's mapped
    /// field.
    /// </summary>
    private static OperationPlanStep? TryFindDeferRequirementProvider(
        ImmutableList<PlanStep> incrementalPlanSteps,
        OperationPlanStep consumingStep,
        OperationRequirement requirement)
    {
        var requirementFieldName =
            requirement.InternalAlias
                ?? ExtractRootFieldName(requirement.Map.ToString());

        if (requirementFieldName is null)
        {
            return null;
        }

        foreach (var step in incrementalPlanSteps)
        {
            if (step is not OperationPlanStep candidate
                || candidate.Id == consumingStep.Id
                || !candidate.Dependents.Contains(consumingStep.Id))
            {
                continue;
            }

            if (!candidate.Target.IsParentOfOrSame(requirement.Path))
            {
                continue;
            }

            if (SelectionSetContainsField(candidate.Definition.SelectionSet, requirementFieldName))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects the response names of the fields that <paramref name="descriptor"/> selects at its
    /// anchor, descending through inline fragments.
    /// </summary>
    private HashSet<string> GetAnchorFieldNames(IncrementalPlanDescriptor descriptor)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        SelectionSetNode anchorSelectionSet;

        if (descriptor.Path.IsRoot)
        {
            anchorSelectionSet = descriptor.Operation.SelectionSet;
        }
        else if (!TryLocateIncrementalPlanAnchor(
            descriptor.Operation,
            descriptor.Path,
            out anchorSelectionSet,
            out _))
        {
            return names;
        }

        CollectResponseNames(anchorSelectionSet, names);
        return names;

        static void CollectResponseNames(SelectionSetNode selectionSet, HashSet<string> names)
        {
            foreach (var selection in selectionSet.Selections)
            {
                switch (selection)
                {
                    case FieldNode field:
                        names.Add(field.Alias?.Value ?? field.Name.Value);
                        break;

                    case InlineFragmentNode fragment:
                        CollectResponseNames(fragment.SelectionSet, names);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Finds every step in <paramref name="incrementalPlanSteps"/> that targets
    /// <paramref name="anchorPath"/> or one of its ancestors and whose every selected field
    /// is consumed by one of its dependents as a requirement that does not lie below
    /// <paramref name="anchorPath"/>. Each of <paramref name="anchorFieldNames"/> stays selected
    /// at <paramref name="anchorPath"/> by a step that is not returned, unless only a step that runs the
    /// mutation selects it.
    /// </summary>
    private ImmutableArray<OperationPlanStep> FindDeferRequirementProducers(
        ImmutableList<PlanStep> incrementalPlanSteps,
        SelectionPath anchorPath,
        HashSet<string> anchorFieldNames)
    {
        var producers = new List<OperationPlanStep>();

        foreach (var step in incrementalPlanSteps)
        {
            if (step is OperationPlanStep operationStep
                && operationStep.SchemaName is not null
                && !operationStep.Dependents.IsEmpty
                && operationStep.Target.IsParentOfOrSame(anchorPath)
                && IsPureRequirementProducer(operationStep, incrementalPlanSteps, anchorPath))
            {
                producers.Add(operationStep);
            }
        }

        // A field of the deferred group that only producers select is kept by the one
        // closest to the anchor. A step that runs the mutation is never kept.
        foreach (var fieldName in anchorFieldNames.Order(StringComparer.Ordinal))
        {
            OperationPlanStep? closestProducer = null;
            var isSelectedByRemainingStep = false;

            foreach (var step in incrementalPlanSteps)
            {
                if (step is not OperationPlanStep { SchemaName: not null } selector
                    || !selector.Target.IsParentOfOrSame(anchorPath)
                    || !SelectsFieldAtPath(selector, anchorPath, fieldName))
                {
                    continue;
                }

                if (!producers.Contains(selector))
                {
                    isSelectedByRemainingStep = true;
                    break;
                }

                if (closestProducer is null || selector.Target.Length > closestProducer.Target.Length)
                {
                    closestProducer = selector;
                }
            }

            if (!isSelectedByRemainingStep
                && closestProducer is { Definition.Operation: not OperationType.Mutation })
            {
                producers.Remove(closestProducer);
            }
        }

        return [.. producers];
    }

    /// <summary>
    /// Determines whether <paramref name="step"/> selects nothing at <paramref name="anchorPath"/>
    /// except leaf fields that its dependents consume as requirements that do not lie below
    /// <paramref name="anchorPath"/>.
    /// </summary>
    private static bool IsPureRequirementProducer(
        OperationPlanStep step,
        ImmutableList<PlanStep> incrementalPlanSteps,
        SelectionPath anchorPath)
    {
        var anchorSelectionSet = LocateSelectionSetAtPath(
            GetStepEntitySelectionSet(step),
            anchorPath,
            step.Target.Length);

        var ownFields = new HashSet<string>(StringComparer.Ordinal);
        if (!TryCollectLeafFieldNames(anchorSelectionSet, ownFields) || ownFields.Count == 0)
        {
            return false;
        }

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependentStepId in step.Dependents)
        {
            if (incrementalPlanSteps.ById(dependentStepId) is not OperationPlanStep dependentStep)
            {
                continue;
            }

            foreach (var (_, requirement) in dependentStep.Requirements)
            {
                // A requirement below the anchor reads a value this step fetches as
                // deferred output, which the enclosing scope does not reach.
                if (!requirement.Path.IsParentOfOrSame(anchorPath)
                    && anchorPath.IsParentOfOrSame(requirement.Path))
                {
                    continue;
                }

                var fieldName = requirement.InternalAlias ?? ExtractRootFieldName(requirement.Map.ToString());

                if (fieldName is not null)
                {
                    consumed.Add(fieldName);
                }
            }
        }

        foreach (var fieldName in ownFields)
        {
            if (!consumed.Contains(fieldName))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collects the response names of the leaf fields of <paramref name="selectionSet"/>,
    /// descending through inline fragments. The <c>__typename</c> discriminator is ignored;
    /// it never satisfies a requirement.
    /// </summary>
    /// <returns>
    /// <c>false</c> when <paramref name="selectionSet"/> selects a composite field.
    /// </returns>
    private static bool TryCollectLeafFieldNames(SelectionSetNode selectionSet, HashSet<string> names)
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode { SelectionSet: not null }:
                    return false;

                case FieldNode field:
                    if (!field.Name.Value.Equals("__typename", StringComparison.Ordinal))
                    {
                        names.Add(field.Alias?.Value ?? field.Name.Value);
                    }
                    break;

                case InlineFragmentNode fragment:
                    if (!TryCollectLeafFieldNames(fragment.SelectionSet, names))
                    {
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes each <paramref name="rerouted"/> provider-to-dependent dependency edge, unless
    /// the dependent still has another requirement that resolves back to the same provider,
    /// and drops the provider's rerouted field once no remaining dependent still consumes it.
    /// A field of <paramref name="anchorFieldNames"/> selected at <paramref name="anchorPath"/> is kept,
    /// unless it is one of <paramref name="heldFields"/>. A held field that a remaining dependent still
    /// consumes is removed from <paramref name="heldFields"/>.
    /// </summary>
    private static ImmutableList<PlanStep> RemoveReroutedInPlanEdges(
        ImmutableList<PlanStep> incrementalPlanSteps,
        List<(int ProviderStepId, int DownstreamStepId, string FieldName)> rerouted,
        SelectionPath anchorPath,
        HashSet<string> anchorFieldNames,
        Dictionary<(int ProviderStepId, string FieldName), OperationRequirement> heldFields)
    {
        if (rerouted.Count == 0)
        {
            return incrementalPlanSteps;
        }

        var removalsByProvider = new Dictionary<int, List<(int DownstreamStepId, string FieldName)>>();
        foreach (var entry in rerouted)
        {
            if (!removalsByProvider.TryGetValue(entry.ProviderStepId, out var removals))
            {
                removals = [];
                removalsByProvider[entry.ProviderStepId] = removals;
            }

            removals.Add((entry.DownstreamStepId, entry.FieldName));
        }

        var updated = ImmutableList.CreateBuilder<PlanStep>();

        foreach (var step in incrementalPlanSteps)
        {
            if (step is OperationPlanStep providerStep
                && removalsByProvider.TryGetValue(providerStep.Id, out var removals))
            {
                var reroutedFieldNamesByDownstream = new Dictionary<int, HashSet<string>>();
                foreach (var (downstreamStepId, fieldName) in removals)
                {
                    if (!reroutedFieldNamesByDownstream.TryGetValue(downstreamStepId, out var fieldNames))
                    {
                        fieldNames = new HashSet<string>(StringComparer.Ordinal);
                        reroutedFieldNamesByDownstream[downstreamStepId] = fieldNames;
                    }

                    fieldNames.Add(fieldName);
                }

                var severedDependentIds = new HashSet<int>();
                foreach (var (downstreamStepId, reroutedFieldNames) in reroutedFieldNamesByDownstream)
                {
                    if (incrementalPlanSteps.ById(downstreamStepId) is OperationPlanStep downstreamStep
                        && DependentStillRequiresProvider(
                            incrementalPlanSteps,
                            downstreamStep,
                            providerStep,
                            reroutedFieldNames))
                    {
                        continue;
                    }

                    severedDependentIds.Add(downstreamStepId);
                }

                var remainingDependents = providerStep.Dependents.Except(severedDependentIds);

                var stillConsumedFieldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var remainingDependentId in remainingDependents)
                {
                    if (incrementalPlanSteps.ById(remainingDependentId) is not OperationPlanStep remainingStep)
                    {
                        continue;
                    }

                    reroutedFieldNamesByDownstream.TryGetValue(remainingDependentId, out var excludedFieldNames);

                    foreach (var (_, requirement) in remainingStep.Requirements)
                    {
                        var fieldName = requirement.InternalAlias ?? ExtractRootFieldName(requirement.Map.ToString());
                        if (fieldName is null || (excludedFieldNames?.Contains(fieldName) ?? false))
                        {
                            continue;
                        }

                        stillConsumedFieldNames.Add(fieldName);
                    }
                }

                var updatedProviderStep = providerStep with { Dependents = remainingDependents };

                foreach (var fieldName in removals.Select(r => r.FieldName).Distinct(StringComparer.Ordinal))
                {
                    var isAnchorField = providerStep.Target.Equals(anchorPath) && anchorFieldNames.Contains(fieldName);

                    if (stillConsumedFieldNames.Contains(fieldName))
                    {
                        heldFields.Remove((providerStep.Id, fieldName));
                    }
                    else if (!isAnchorField || heldFields.ContainsKey((providerStep.Id, fieldName)))
                    {
                        updatedProviderStep = RemoveUnconsumedEntityField(updatedProviderStep, fieldName);
                    }
                }

                updated.Add(updatedProviderStep);
            }
            else
            {
                updated.Add(step);
            }
        }

        return updated.ToImmutable();
    }

    /// <summary>
    /// Determines whether <paramref name="downstreamStep"/> still has a requirement, other
    /// than those named in <paramref name="reroutedFieldNames"/>, that
    /// <see cref="TryFindDeferRequirementProvider"/> resolves back to <paramref name="providerStep"/>.
    /// </summary>
    private static bool DependentStillRequiresProvider(
        ImmutableList<PlanStep> incrementalPlanSteps,
        OperationPlanStep downstreamStep,
        OperationPlanStep providerStep,
        HashSet<string> reroutedFieldNames)
    {
        foreach (var (_, requirement) in downstreamStep.Requirements)
        {
            var fieldName = requirement.InternalAlias ?? ExtractRootFieldName(requirement.Map.ToString());
            if (fieldName is not null && reroutedFieldNames.Contains(fieldName))
            {
                continue;
            }

            if (TryFindDeferRequirementProvider(incrementalPlanSteps, downstreamStep, requirement)?.Id
                == providerStep.Id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes a single top-level field from <paramref name="step"/>'s own entity-level
    /// selection set (see <see cref="GetStepEntitySelectionSet"/>) once nothing consumes
    /// it any longer, keeping the step's selection-set index in sync.
    /// </summary>
    private static OperationPlanStep RemoveUnconsumedEntityField(OperationPlanStep step, string fieldName)
    {
        var entitySelectionSet = GetStepEntitySelectionSet(step);
        var filtered = new List<ISelectionNode>(entitySelectionSet.Selections.Count);
        var removed = false;

        foreach (var selection in entitySelectionSet.Selections)
        {
            if (!removed
                && selection is FieldNode field
                && (field.Alias?.Value ?? field.Name.Value).Equals(fieldName, StringComparison.Ordinal))
            {
                removed = true;
                continue;
            }

            filtered.Add(selection);
        }

        if (!removed || filtered.Count == 0)
        {
            return step;
        }

        return WithEntitySelectionSet(step, new SelectionSetNode(filtered));
    }

    /// <summary>
    /// Returns a copy of <paramref name="step"/> whose own entity-level selection set (see
    /// <see cref="GetStepEntitySelectionSet"/>) is replaced with <paramref name="newEntitySelectionSet"/>,
    /// keeping the step's selection-set index in sync.
    /// </summary>
    private static OperationPlanStep WithEntitySelectionSet(
        OperationPlanStep step,
        SelectionSetNode newEntitySelectionSet)
    {
        var newDefinition =
            step.Lookup is not null && step.Definition.SelectionSet.Selections is [FieldNode lookupField]
                ? step.Definition.WithSelectionSet(
                    new SelectionSetNode([lookupField.WithSelectionSet(newEntitySelectionSet)]))
                : step.Definition.WithSelectionSet(newEntitySelectionSet);

        var index = SelectionSetIndexer.Create(newDefinition);

        return step with
        {
            Definition = newDefinition,
            SelectionSets = SelectionSetIndexer.CreateIdSet(newDefinition.SelectionSet, index),
            RootSelectionSetId = index.GetId(newDefinition.SelectionSet)
        };
    }

    /// <summary>
    /// Removes steps that moved to the parent scope and records parent
    /// dependencies on the remaining steps.
    /// </summary>
    private static ImmutableList<PlanStep> RewriteIncrementalPlanAfterDeferRequirementRouting(
        ImmutableList<PlanStep> incrementalPlanSteps,
        List<LiftedDeferRequirement> lifted,
        HashSet<int> droppedStepIds)
    {
        var parentRefsByStepId = new Dictionary<int, ImmutableHashSet<ParentStepRef>.Builder>();
        foreach (var entry in lifted)
        {
            if (!parentRefsByStepId.TryGetValue(entry.DownstreamStepId, out var builder))
            {
                builder = ImmutableHashSet.CreateBuilder<ParentStepRef>();
                parentRefsByStepId[entry.DownstreamStepId] = builder;
            }

            builder.Add(new ParentStepRef(entry.ParentStepId));
        }

        var survivors = new List<PlanStep>(incrementalPlanSteps.Count - droppedStepIds.Count);
        var oldToNewId = new Dictionary<int, int>(incrementalPlanSteps.Count - droppedStepIds.Count);

        foreach (var step in incrementalPlanSteps)
        {
            if (droppedStepIds.Contains(step.Id))
            {
                continue;
            }

            oldToNewId[step.Id] = survivors.Count + 1;
            survivors.Add(step);
        }

        var rewritten = ImmutableList.CreateBuilder<PlanStep>();

        foreach (var step in survivors)
        {
            if (step is OperationPlanStep operationStep)
            {
                var newDependents = RenumberDeferDependents(operationStep.Dependents, droppedStepIds, oldToNewId);
                var newParentDependencies = operationStep.ParentDependencies;

                if (parentRefsByStepId.TryGetValue(operationStep.Id, out var parentRefBuilder))
                {
                    newParentDependencies = newParentDependencies.Union(parentRefBuilder.ToImmutable());
                }

                rewritten.Add(operationStep with
                {
                    Id = oldToNewId[operationStep.Id],
                    Dependents = newDependents,
                    ParentDependencies = newParentDependencies
                });
            }
            else
            {
                rewritten.Add(step with { Id = oldToNewId[step.Id] });
            }
        }

        return rewritten.ToImmutable();
    }

    private static ImmutableHashSet<int> RenumberDeferDependents(
        ImmutableHashSet<int> dependents,
        HashSet<int> removedStepIds,
        Dictionary<int, int> oldToNewId)
    {
        if (dependents.IsEmpty)
        {
            return dependents;
        }

        var builder = ImmutableHashSet.CreateBuilder<int>();
        foreach (var dependentId in dependents)
        {
            if (removedStepIds.Contains(dependentId))
            {
                continue;
            }

            if (oldToNewId.TryGetValue(dependentId, out var newId))
            {
                builder.Add(newId);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns the selection set that holds <paramref name="step"/>'s own entity-level
    /// fields. A keyed-lookup step's <see cref="OperationPlanStep.Definition"/> wraps
    /// those fields inside a synthetic lookup field (for example
    /// <c>userById(id: $x) { ... }</c>) that has no corresponding <see cref="SelectionPath"/>
    /// segment; the entity's own fields start one level inside that wrapper. A step with
    /// no lookup (a root fetch, or a step reusing the client's own path) has no such
    /// wrapper, so its definition's top-level selection set already sits at
    /// <see cref="OperationPlanStep.Target"/>.
    /// </summary>
    private static SelectionSetNode GetStepEntitySelectionSet(OperationPlanStep step)
        => step.Lookup is not null
            && step.Definition.SelectionSet.Selections is [FieldNode { SelectionSet: { } lookupSelectionSet }]
                ? lookupSelectionSet
                : step.Definition.SelectionSet;

    /// <summary>
    /// Returns the GraphQL type of <paramref name="step"/>'s own entity-level selection
    /// set (see <see cref="GetStepEntitySelectionSet"/>).
    /// </summary>
    private ITypeDefinition GetStepEntityType(OperationPlanStep step)
        => step.Lookup is not null
            && step.Definition.SelectionSet.Selections is [FieldNode { SelectionSet: not null }]
                ? step.Type
                : _schema.GetOperationType(step.Definition.Operation);

    /// <summary>
    /// Walks <paramref name="parentStep"/>'s definition to locate the selection
    /// set at <paramref name="path"/> and resolves its GraphQL type.
    /// </summary>
    private bool TryLocateDeferRequirementTarget(
        OperationPlanStep parentStep,
        SelectionPath path,
        out SelectionSetNode targetSelectionSet,
        out ITypeDefinition targetType)
        => TryLocateSelectionSetAtPath(
            GetStepEntitySelectionSet(parentStep),
            GetStepEntityType(parentStep),
            path,
            startIndex: parentStep.Target.Length,
            out targetSelectionSet,
            out targetType);

    /// <summary>
    /// Walks <paramref name="startSet"/> from <paramref name="path"/>'s <paramref name="startIndex"/>
    /// segment onward, following fields and inline fragments, to locate the selection set at
    /// <paramref name="path"/> and resolve its GraphQL type.
    /// </summary>
    private bool TryLocateSelectionSetAtPath(
        SelectionSetNode startSet,
        ITypeDefinition startType,
        SelectionPath path,
        int startIndex,
        out SelectionSetNode targetSelectionSet,
        out ITypeDefinition targetType)
    {
        var currentSet = startSet;
        var currentType = startType;

        for (var i = startIndex; i < path.Length; i++)
        {
            var segment = path[i];

            switch (segment.Kind)
            {
                case SelectionPathSegmentKind.Field:
                    FieldNode? fieldNode = null;
                    foreach (var selection in currentSet.Selections)
                    {
                        if (selection is FieldNode candidate
                            && (candidate.Alias?.Value == segment.Name || candidate.Name.Value == segment.Name))
                        {
                            fieldNode = candidate;
                            break;
                        }
                    }

                    if (fieldNode?.SelectionSet is null
                        || currentType is not FusionComplexTypeDefinition complexType
                        || !complexType.Fields.TryGetField(
                            fieldNode.Name.Value,
                            allowInaccessibleFields: true,
                            out var field))
                    {
                        targetSelectionSet = null!;
                        targetType = null!;
                        return false;
                    }

                    currentSet = fieldNode.SelectionSet;
                    currentType = field.Type.NamedType();
                    break;

                case SelectionPathSegmentKind.InlineFragment:
                    InlineFragmentNode? fragment = null;
                    foreach (var selection in currentSet.Selections)
                    {
                        if (selection is InlineFragmentNode candidate
                            && candidate.TypeCondition?.Name.Value == segment.Name)
                        {
                            fragment = candidate;
                            break;
                        }
                    }

                    if (fragment is null
                        || !_schema.Types.TryGetType(
                            segment.Name,
                            allowInaccessibleFields: true,
                            out var fragmentType))
                    {
                        targetSelectionSet = null!;
                        targetType = null!;
                        return false;
                    }

                    currentSet = fragment.SelectionSet;
                    currentType = fragmentType;
                    break;

                default:
                    targetSelectionSet = null!;
                    targetType = null!;
                    return false;
            }
        }

        targetSelectionSet = currentSet;
        targetType = currentType;
        return true;
    }

    private static SelectionSetNode LocateSelectionSetAtPath(
        SelectionSetNode root,
        SelectionPath path,
        int startIndex)
    {
        var currentSet = root;

        for (var i = startIndex; i < path.Length; i++)
        {
            var segment = path[i];

            switch (segment.Kind)
            {
                case SelectionPathSegmentKind.Field:
                    foreach (var selection in currentSet.Selections)
                    {
                        if (selection is FieldNode candidate
                            && (candidate.Alias?.Value == segment.Name || candidate.Name.Value == segment.Name)
                            && candidate.SelectionSet is { } inner)
                        {
                            currentSet = inner;
                            break;
                        }
                    }
                    break;

                case SelectionPathSegmentKind.InlineFragment:
                    foreach (var selection in currentSet.Selections)
                    {
                        if (selection is InlineFragmentNode candidate
                            && candidate.TypeCondition?.Name.Value == segment.Name)
                        {
                            currentSet = candidate.SelectionSet;
                            break;
                        }
                    }
                    break;
            }
        }

        return currentSet;
    }

    private static bool SelectionSetContainsField(
        SelectionSetNode selectionSet,
        string fieldName)
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field
                    when (field.Alias?.Value ?? field.Name.Value).Equals(fieldName, StringComparison.Ordinal):
                    return true;

                case FieldNode { SelectionSet: { } nested }:
                    if (SelectionSetContainsField(nested, fieldName))
                    {
                        return true;
                    }
                    break;

                case InlineFragmentNode fragment:
                    if (SelectionSetContainsField(fragment.SelectionSet, fieldName))
                    {
                        return true;
                    }
                    break;
            }
        }

        return false;
    }

    private static string? ExtractRootFieldName(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == ' ' || c == '{' || c == '.')
            {
                return i == 0 ? null : text[..i];
            }
        }

        return text;
    }

    private static InvalidOperationException CreateUnsatisfiableDeferRequirementException(
        OperationPlanStep selfFetch,
        OperationRequirement requirement,
        string reason)
        => new(
            $"The deferred incremental plan's requirement '{requirement.Key}' at path "
            + $"'{requirement.Path}' could not be resolved from the parent plan. "
            + $"Defer anchor: '{selfFetch.Target}'. "
            + $"Target schema: '{selfFetch.SchemaName}'. "
            + $"Reason: {reason}.");

    private readonly record struct LiftedDeferRequirement(
        OperationRequirement Requirement,
        int DownstreamStepId,
        int ParentStepId,
        ScopeState Scope);

    /// <summary>
    /// Builds execution nodes for an incremental plan's plan steps.
    /// </summary>
    private (ImmutableArray<ExecutionNode> RootNodes, ImmutableArray<ExecutionNode> AllNodes) BuildDeferredExecutionNodes(
        OperationDefinitionNode deferredOperation,
        ImmutableList<PlanStep> planSteps,
        int nextNodeId,
        CancellationToken cancellationToken)
    {
        if (planSteps.Count == 0)
        {
            return ([], []);
        }

        var ctx = new ExecutionPlanBuildContext(nextNodeId);
        var hasVariables = deferredOperation.VariableDefinitions.Count > 0;

        planSteps = TransformPlanSteps(planSteps, deferredOperation);
        IndexDependencies(planSteps, ctx);
        BuildExecutionNodes(planSteps, ctx, _schema, hasVariables, cancellationToken);
        MergeAndBatchOperations(
            ctx,
            _options.EnableRequestGrouping,
            _options.MergePolicy,
            splitForEntityReuse: false,
            serialScopes: null,
            _schema);
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

        foreach (var node in allNodes)
        {
            node.Seal();
        }

        return (rootNodes, allNodes);
    }
}
