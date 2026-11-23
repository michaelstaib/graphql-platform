using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Planning.Partitioners;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// An immutable backlog of work items paired with cost tracking.
/// Every push and pop keeps the cost in sync with the stack,
/// so branch scoring stays O(1) per transition.
/// </summary>
internal readonly struct Backlog(
    ImmutableStack<WorkItem> items,
    BacklogCost cost,
    IReadOnlyDictionary<DirectiveNode, int>? deferredFragments)
{
    private readonly ImmutableStack<WorkItem> _items = items;
    private readonly IReadOnlyDictionary<DirectiveNode, int>? _deferredFragments = deferredFragments;

    public static Backlog Empty { get; } = new([], BacklogCost.Empty, null);

    /// <summary>
    /// The cost tracking state for all items in this backlog.
    /// </summary>
    public BacklogCost Cost { get; } = cost;

    /// <summary>
    /// Whether this backlog has no work items left.
    /// </summary>
    public bool IsEmpty => _items.IsEmpty;

    /// <summary>
    /// Returns the next work item without removing it.
    /// </summary>
    public WorkItem Peek() => _items.Peek();

    /// <summary>
    /// Pushes a work item and updates cost tracking.
    /// </summary>
    public Backlog Push(WorkItem workItem)
    {
        var cost = PlannerCostEstimator.AddWorkItemCost(Cost, workItem);
        return new Backlog(_items.Push(workItem), cost, _deferredFragments);
    }

    /// <summary>
    /// Pops the next work item and updates cost tracking.
    /// </summary>
    public Backlog Pop(out WorkItem workItem)
    {
        var items = _items.Pop(out workItem);
        var cost = PlannerCostEstimator.RemoveWorkItemCost(Cost, workItem);
        return new Backlog(items, cost, _deferredFragments);
    }

    /// <summary>
    /// Determines whether a work item of the backlog has the step with the id <paramref name="stepId"/>
    /// as a dependent.
    /// </summary>
    public bool HasWorkItemWithDependent(int stepId)
    {
        foreach (var workItem in _items)
        {
            if (workItem.Dependents.Contains(stepId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps the <c>@defer</c> directive of a fragment to the id of its delivery group, or <c>null</c> when
    /// the operation is not planned with marked steps.
    /// </summary>
    public IReadOnlyDictionary<DirectiveNode, int>? DeferredFragments => _deferredFragments;

    /// <summary>
    /// Returns a backlog that marks the work items it spills from <c>@defer</c> fragments with the delivery
    /// group of the fragment.
    /// </summary>
    public Backlog WithDeferredFragments(IReadOnlyDictionary<DirectiveNode, int> deferredFragments)
        => new(_items, Cost, deferredFragments);

    /// <summary>
    /// Returns the conditions of a work item that is created while planning a work item with
    /// <paramref name="parentConditions"/>, or <paramref name="conditions"/> unchanged when the operation is not
    /// planned with marked steps.
    /// </summary>
    public ExecutionNodeCondition[] InheritConditions(
        ExecutionNodeCondition[] parentConditions,
        ExecutionNodeCondition[] conditions)
    {
        if (_deferredFragments is null || parentConditions.Length == 0)
        {
            return conditions;
        }

        if (conditions.Length == 0)
        {
            return parentConditions;
        }

        var combined = new List<ExecutionNodeCondition>(parentConditions.Length + conditions.Length);
        combined.AddRange(parentConditions);

        foreach (var condition in conditions)
        {
            if (!combined.Contains(condition))
            {
                combined.Add(condition);
            }
        }

        return combined.Count == parentConditions.Length ? parentConditions : [.. combined];
    }

    public Backlog BindSourceSchemaNodeCandidate(int candidateGroupId, string schemaName)
    {
        var workItems = _items.ToArray();
        var items = ImmutableStack<WorkItem>.Empty;

        for (var i = workItems.Length - 1; i >= 0; i--)
        {
            var workItem = workItems[i];
            if (workItem.SourceSchemaNodePolicy is { CandidateGroupId: { } groupId } policy
                && groupId == candidateGroupId)
            {
                workItem = workItem with
                {
                    SourceSchemaNodePolicy = policy.BindCandidate(schemaName)
                };
            }

            items = items.Push(workItem);
        }

        return new Backlog(items, Cost, _deferredFragments);
    }

    /// <summary>
    /// Pushes work items for unresolvable selection sets that need
    /// their own operation steps on other schemas.
    /// </summary>
    /// <param name="unresolvable">The unresolvable selection sets.</param>
    /// <param name="fromSchema">The source schema that could not resolve them.</param>
    /// <param name="parentDepth">The depth of the step that produced them.</param>
    /// <param name="deliveryGroups">
    /// The delivery groups that every work item carries unless its selection set is the content of a
    /// <c>@defer</c> fragment.
    /// </param>
    /// <param name="conditions">The conditions of the work item the selection sets were found in.</param>
    /// <param name="dependents">The steps that need the data of the selection sets.</param>
    /// <param name="allowSourceSchemaReentry">Whether the work items may re-enter the source schema.</param>
    /// <param name="sourceSchemaNodePolicy">The source schema node planning policy, or null.</param>
    public Backlog PushUnresolvable(
        ImmutableStack<ConditionedSelectionSet> unresolvable,
        string fromSchema,
        int parentDepth,
        ImmutableArray<int> deliveryGroups,
        ExecutionNodeCondition[] conditions,
        ImmutableHashSet<int>? dependents = null,
        bool allowSourceSchemaReentry = false,
        SourceSchemaNodePlanningPolicy? sourceSchemaNodePolicy = null)
    {
        if (unresolvable.IsEmpty)
        {
            return this;
        }

        var backlog = this;

        foreach (var entry in unresolvable.Reverse())
        {
            var selectionSet = entry.SelectionSet;
            var entryDeliveryGroups = deliveryGroups;
            var entryAllowsReentry = allowSourceSchemaReentry;

            if (entry.DeliveryGroup is { } deliveryGroupId)
            {
                entryDeliveryGroups = [deliveryGroupId];
                entryAllowsReentry = true;
            }

            var workItem = new OperationWorkItem(
                selectionSet.Path.IsRoot
                    ? OperationWorkItemKind.Root
                    : OperationWorkItemKind.Lookup,
                selectionSet,
                FromSchema: fromSchema)
            {
                ParentDepth = parentDepth,
                Conditions = _deferredFragments is null
                    ? entry.Conditions
                    : InheritConditions(
                        InheritConditions(conditions, entry.Conditions),
                        GetSharedSelectionConditions(selectionSet.Node)),
                Dependents = dependents ?? [],
                AllowSourceSchemaReentry = entryAllowsReentry,
                SourceSchemaNodePolicy = sourceSchemaNodePolicy,
                DeliveryGroups = entryDeliveryGroups
            };
            backlog = backlog.Push(workItem);
        }

        return backlog;
    }

    // Returns the conditions that every top-level selection of the selection set carries.
    private static ExecutionNodeCondition[] GetSharedSelectionConditions(SelectionSetNode selectionSet)
    {
        ExecutionNodeCondition[]? shared = null;

        foreach (var selection in selectionSet.Selections)
        {
            var directives = selection switch
            {
                FieldNode field => field.Directives,
                InlineFragmentNode fragment => fragment.Directives,
                _ => []
            };
            var conditions = SelectionSetPartitioner.ExtractDirectiveConditions(directives) ?? [];

            shared = shared is null
                ? conditions
                : [.. shared.Where(condition => conditions.Contains(condition))];

            if (shared.Length == 0)
            {
                return [];
            }
        }

        return shared ?? [];
    }

    /// <summary>
    /// Pushes work items for fields that have requirements
    /// which may need lookup steps to satisfy.
    /// </summary>
    /// <param name="fieldsWithRequirements">The fields that have requirements.</param>
    /// <param name="consumer">The owner of the fields.</param>
    /// <param name="dependents">The steps that need the data of the fields.</param>
    /// <param name="parentDepth">The depth of the step that produced the fields.</param>
    /// <param name="deliveryGroups">The delivery groups of the work item the fields were found in.</param>
    /// <param name="conditions">The conditions of the work item the fields were found in.</param>
    /// <param name="sourceSchemaNodePolicy">The source schema node planning policy, or null.</param>
    public Backlog PushRequirements(
        ImmutableStack<ConditionedFieldSelection> fieldsWithRequirements,
        RequirementConsumer consumer,
        ImmutableHashSet<int> dependents,
        int parentDepth,
        ImmutableArray<int> deliveryGroups,
        ExecutionNodeCondition[] conditions,
        SourceSchemaNodePlanningPolicy? sourceSchemaNodePolicy = null)
    {
        if (fieldsWithRequirements.IsEmpty)
        {
            return this;
        }

        var backlog = this;

        foreach (var entry in fieldsWithRequirements.Reverse())
        {
            var workItem = new FieldRequirementWorkItem(entry.FieldSelection, consumer)
            {
                Dependents = dependents,
                ParentDepth = parentDepth,
                Conditions = InheritConditions(conditions, entry.Conditions),
                DeliveryGroups = deliveryGroups,
                SourceSchemaNodePolicy = sourceSchemaNodePolicy
            };
            backlog = backlog.Push(workItem);
        }

        return backlog;
    }
}
