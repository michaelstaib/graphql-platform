using System.Collections.Immutable;
using HotChocolate.Fusion.Planning.Partitioners;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// An immutable backlog of work items paired with cost tracking.
/// Every push and pop keeps the cost in sync with the stack,
/// so branch scoring stays O(1) per transition.
/// </summary>
internal readonly struct Backlog(
    ImmutableStack<WorkItem> items,
    BacklogCost cost,
    DeferredFragmentIndex? deferredFragments)
{
    private readonly ImmutableStack<WorkItem> _items = items;
    private readonly DeferredFragmentIndex? _deferredFragments = deferredFragments;

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
    /// Returns a backlog that marks the work items it spills from <c>@defer</c> fragments.
    /// </summary>
    public Backlog WithDeferredFragments(DeferredFragmentIndex deferredFragments)
        => new(_items, Cost, deferredFragments);

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
    /// <param name="index">
    /// The selection set index to find <c>@defer</c> fragments with, or <c>null</c> when the selection sets
    /// cannot be the content of a <c>@defer</c> fragment.
    /// </param>
    /// <param name="deliveryGroups">
    /// The delivery groups that every work item carries unless its selection set is the content of a
    /// <c>@defer</c> fragment of another group.
    /// </param>
    /// <param name="dependents">The steps that need the data of the selection sets.</param>
    /// <param name="allowSourceSchemaReentry">Whether the work items may re-enter the source schema.</param>
    /// <param name="sourceSchemaNodePolicy">The source schema node planning policy, or null.</param>
    public Backlog PushUnresolvable(
        ImmutableStack<ConditionedSelectionSet> unresolvable,
        string fromSchema,
        int parentDepth,
        ISelectionSetIndex? index,
        ImmutableArray<int> deliveryGroups,
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

            if (index is not null
                && _deferredFragments is not null
                && _deferredFragments.TryGetDeliveryGroup(index, selectionSet.Id, out var deliveryGroupId)
                && !deliveryGroups.Contains(deliveryGroupId))
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
                Conditions = entry.Conditions,
                Dependents = dependents ?? [],
                AllowSourceSchemaReentry = entryAllowsReentry,
                SourceSchemaNodePolicy = sourceSchemaNodePolicy,
                DeliveryGroups = entryDeliveryGroups
            };
            backlog = backlog.Push(workItem);
        }

        return backlog;
    }

    /// <summary>
    /// Pushes work items for fields that have requirements
    /// which may need lookup steps to satisfy.
    /// </summary>
    /// <param name="fieldsWithRequirements">The fields that have requirements.</param>
    /// <param name="consumer">The owner of the fields.</param>
    /// <param name="dependents">The steps that need the data of the fields.</param>
    /// <param name="parentDepth">The depth of the step that produced the fields.</param>
    /// <param name="sourceSchemaNodePolicy">The source schema node planning policy, or null.</param>
    public Backlog PushRequirements(
        ImmutableStack<ConditionedFieldSelection> fieldsWithRequirements,
        RequirementConsumer consumer,
        ImmutableHashSet<int> dependents,
        int parentDepth,
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
                Conditions = entry.Conditions,
                SourceSchemaNodePolicy = sourceSchemaNodePolicy
            };
            backlog = backlog.Push(workItem);
        }

        return backlog;
    }
}
