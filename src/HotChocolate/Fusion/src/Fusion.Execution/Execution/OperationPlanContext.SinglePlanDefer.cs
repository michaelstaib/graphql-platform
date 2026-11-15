using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Execution;

public sealed partial class OperationPlanContext
{
    private SinglePlanDeliveryState? _singlePlanDelivery;

    /// <summary>
    /// Gets the delivery state of a request whose plan has marked steps, or <c>null</c>.
    /// </summary>
    internal SinglePlanDeliveryState? SinglePlanDelivery => _singlePlanDelivery;

    /// <summary>
    /// Starts the delivery of a plan with marked steps on the result document of this context.
    /// </summary>
    internal SinglePlanDeliveryState EnableSinglePlanDelivery(OperationPlan plan)
    {
        var state = new SinglePlanDeliveryState(
            plan,
            IncludeConditionFlags,
            DeferConditionFlags,
            DeliveryGroup.GetActive(plan.DeliveryGroups, Variables));
        _singlePlanDelivery = state;
        _resultStore.Result.AttachDelivery(state);
        return state;
    }

    /// <summary>
    /// Restricts the rows that the merge of a marked step may write to the rows of its delivery groups.
    /// </summary>
    internal void SetWriteFilter(ImmutableArray<int> deliveryGroups)
        => _resultStore.SetWriteFilter(deliveryGroups);

    internal void ClearWriteFilter()
        => _resultStore.ClearWriteFilter();

    /// <summary>
    /// Gets the errors of the result that were recorded after the first <paramref name="count"/> errors.
    /// </summary>
    internal ImmutableList<IError> GetErrorsAfter(int count)
    {
        var errors = _resultStore.Errors;

        if (errors is null || errors.Count <= count)
        {
            return [];
        }

        var builder = ImmutableList.CreateBuilder<IError>();

        for (var i = count; i < errors.Count; i++)
        {
            builder.Add(errors[i]);
        }

        return builder.ToImmutable();
    }

    internal CompositeResultDocument ResultDocument => _resultStore.Result;

    internal int ErrorCount => _resultStore.Errors?.Count ?? 0;
}

/// <summary>
/// The per request state of a plan with marked steps: where each field is delivered, which steps are
/// needed and how far each delivery group is.
/// </summary>
internal sealed class SinglePlanDeliveryState
{
    private readonly Dictionary<int, DeliveryResolution> _resolutions = [];
    private readonly Dictionary<int, DeliveryBlock> _blocks = [];
    private readonly object _sync = new();
    private readonly List<ExecutionNode> _markedNodes = [];
    private readonly Dictionary<SelectionPath, SelectionSet[]> _targetSets = [];

    public SinglePlanDeliveryState(
        OperationPlan plan,
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        ConditionFlags activeGroups)
    {
        Plan = plan;
        IncludeFlags = includeFlags;
        DeferFlags = deferFlags;
        ActiveGroups = activeGroups;

        var groupCount = 0;

        foreach (var group in plan.DeliveryGroups)
        {
            groupCount = Math.Max(groupCount, group.Id + 1);
        }

        Honored = new bool[groupCount];
        Announced = new bool[groupCount];
        Completed = new bool[groupCount];
        NodesByGroup = new List<ExecutionNode>[groupCount];
        Needed = new bool[plan.MaxNodeId + 1];
        Finished = new bool[plan.MaxNodeId + 1];
        Failed = new bool[plan.MaxNodeId + 1];

        for (var i = 0; i < groupCount; i++)
        {
            NodesByGroup[i] = [];
        }

        foreach (var node in plan.AllNodes)
        {
            var groups = GetDeliveryGroups(node);

            if (groups.IsDefaultOrEmpty)
            {
                continue;
            }

            _markedNodes.Add(node);

            foreach (var group in groups)
            {
                NodesByGroup[group].Add(node);
            }
        }
    }

    public OperationPlan Plan { get; }

    public ConditionFlags IncludeFlags { get; }

    public ConditionFlags DeferFlags { get; }

    public ConditionFlags ActiveGroups { get; }

    public IReadOnlyList<ExecutionNode> MarkedNodes => _markedNodes;

    public bool[] Honored { get; }

    public bool[] Announced { get; }

    public bool[] Completed { get; }

    public List<ExecutionNode>[] NodesByGroup { get; }

    public bool[] Needed { get; }

    public bool[] Finished { get; }

    public bool[] Failed { get; }

    /// <summary>
    /// Gets the number of errors that payloads already carry.
    /// </summary>
    public int PublishedErrorCount { get; set; }

    /// <summary>
    /// Gets the ids of the delivery groups a step carries, or an empty array when the step belongs to
    /// the initial payload.
    /// </summary>
    public static ImmutableArray<int> GetDeliveryGroups(ExecutionNode node)
    {
        switch (node)
        {
            case OperationExecutionNode operation:
                return operation.DeliveryGroups;

            case OperationBatchExecutionNode batch:
                var groups = ImmutableArray<int>.Empty;

                foreach (var definition in batch.Operations)
                {
                    foreach (var group in definition.DeliveryGroups)
                    {
                        if (!groups.Contains(group))
                        {
                            groups = groups.Add(group);
                        }
                    }
                }

                return groups;

            default:
                return default;
        }
    }

    /// <summary>
    /// Gets the row offsets of an object block of <paramref name="selections"/> that are not part of the
    /// initial payload.
    /// </summary>
    public DeliveryBlock GetBlock(ReadOnlySpan<Selection> selections)
    {
        var selectionSetId = selections[0].DeclaringSelectionSet.Id;

        lock (_sync)
        {
            if (_blocks.TryGetValue(selectionSetId, out var block))
            {
                return block;
            }
        }

        var excluded = new List<int>();
        var held = new List<int>();

        for (var i = 0; i < selections.Length; i++)
        {
            var resolution = Resolve(selections[i]);

            if (resolution.Kind is DeliveryKind.Initial)
            {
                continue;
            }

            excluded.Add((i * 2) + 1);

            if (resolution.Kind is DeliveryKind.Groups)
            {
                held.Add((i * 2) + 1);
            }
        }

        var created = new DeliveryBlock(excluded.ToArray(), held.ToArray());

        lock (_sync)
        {
            _blocks[selectionSetId] = created;
        }

        return created;
    }

    /// <summary>
    /// Gets where a selection is delivered for this request.
    /// </summary>
    public DeliveryResolution Resolve(Selection selection)
    {
        lock (_sync)
        {
            if (!_resolutions.TryGetValue(selection.Id, out var resolution))
            {
                resolution = selection.ResolveDelivery(IncludeFlags, DeferFlags);
                _resolutions.Add(selection.Id, resolution);
            }

            return resolution;
        }
    }

    public static bool Contains(DeliveryResolution resolution, int groupId)
    {
        foreach (var group in resolution.Groups)
        {
            if (group.Id == groupId)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Intersects(DeliveryResolution resolution, ImmutableArray<int> groupIds)
    {
        foreach (var group in resolution.Groups)
        {
            if (groupIds.Contains(group.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decides which marked steps run for this request and which delivery groups are honored.
    /// A step is needed when one of its top level fields resolves to one of its groups, or when a step that
    /// depends on it is needed. A group is honored when it is active and a needed step delivers a field to it.
    /// </summary>
    public void ComputeNeeded()
    {
        var plan = Plan;

        foreach (var node in _markedNodes)
        {
            var groups = GetDeliveryGroups(node);

            if (!NeedsAnyGroup(node, groups, out var honoredGroups))
            {
                continue;
            }

            Needed[node.Id] = true;

            foreach (var group in honoredGroups)
            {
                Honored[group] = true;
            }
        }

        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var node in _markedNodes)
            {
                if (!Needed[node.Id])
                {
                    continue;
                }

                foreach (var dependency in node.Dependencies)
                {
                    var dependencyNode = plan.GetExecutionNode(dependency);

                    if (!Needed[dependencyNode.Id] && !GetDeliveryGroups(dependencyNode).IsDefaultOrEmpty)
                    {
                        Needed[dependencyNode.Id] = true;
                        changed = true;
                    }
                }
            }
        }
    }

    private bool NeedsAnyGroup(ExecutionNode node, ImmutableArray<int> groups, out List<int> honored)
    {
        honored = [];

        switch (node)
        {
            case OperationExecutionNode operation:
                CollectHonored(operation.Target, operation.ResultSelectionSet, groups, honored);
                break;

            case OperationBatchExecutionNode batch:
                foreach (var definition in batch.Operations)
                {
                    switch (definition)
                    {
                        case SingleOperationDefinition single:
                            CollectHonored(single.Target, single.ResultSelectionSet, definition.DeliveryGroups, honored);
                            break;

                        case BatchOperationDefinition many:
                            foreach (var target in many.Targets)
                            {
                                CollectHonored(target, many.ResultSelectionSet, definition.DeliveryGroups, honored);
                            }

                            break;
                    }
                }

                break;

            default:
                return false;
        }

        return honored.Count > 0;
    }

    private void CollectHonored(
        SelectionPath target,
        ResultSelectionSet resultSelectionSet,
        ImmutableArray<int> groups,
        List<int> honored)
    {
        foreach (var selectionSet in GetTargetSelectionSets(target))
        {
            CollectHonored(selectionSet, resultSelectionSet, groups, honored);
        }
    }

    private void CollectHonored(
        SelectionSet selectionSet,
        ResultSelectionSet resultSelectionSet,
        ImmutableArray<int> groups,
        List<int> honored)
    {
        foreach (var selection in resultSelectionSet.Selections)
        {
            if (!selectionSet.TryGetSelection(selection.ResponseName, out var field))
            {
                continue;
            }

            foreach (var group in Resolve(field).Groups)
            {
                if (groups.Contains(group.Id) && ActiveGroups.IsSet(group.Id) && !honored.Contains(group.Id))
                {
                    honored.Add(group.Id);
                }
            }
        }

        foreach (var fragment in resultSelectionSet.Fragments)
        {
            CollectHonored(selectionSet, fragment.Body, groups, honored);
        }
    }

    private SelectionSet[] GetTargetSelectionSets(SelectionPath path)
    {
        if (_targetSets.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var operation = Plan.Operation;
        var current = new List<SelectionSet> { operation.RootSelectionSet };
        var selections = new List<Selection>();

        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            if (segment.Kind is SelectionPathSegmentKind.Root)
            {
                continue;
            }

            if (segment.Kind is SelectionPathSegmentKind.InlineFragment)
            {
                var typeName = segment.Name;
                current.Clear();

                foreach (var selection in selections)
                {
                    foreach (var type in operation.GetPossibleTypes(selection))
                    {
                        if (type.Name == typeName)
                        {
                            current.Add(operation.GetSelectionSet(selection, type));
                        }
                    }
                }

                selections.Clear();
                continue;
            }

            if (selections.Count > 0)
            {
                ExpandPossibleTypes(selections, current);
            }

            selections.Clear();

            foreach (var selectionSet in current)
            {
                if (selectionSet.TryGetSelection(segment.Name, out var selection))
                {
                    selections.Add(selection);
                }
            }

            current.Clear();
        }

        if (selections.Count > 0)
        {
            ExpandPossibleTypes(selections, current);
        }

        var result = current.ToArray();
        _targetSets[path] = result;
        return result;
    }

    private void ExpandPossibleTypes(List<Selection> selections, List<SelectionSet> output)
    {
        output.Clear();

        foreach (var selection in selections)
        {
            foreach (var type in Plan.Operation.GetPossibleTypes(selection))
            {
                output.Add(Plan.Operation.GetSelectionSet(selection, type));
            }
        }
    }

    /// <summary>
    /// Gets the nearest honored delivery group above <paramref name="group"/>, or <c>null</c>.
    /// </summary>
    public DeliveryGroup? GetHonoredParent(DeliveryGroup group)
    {
        for (var parent = group.Parent; parent is not null; parent = parent.Parent)
        {
            if (Honored[parent.Id])
            {
                return parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether every step that carries <paramref name="groupId"/> is done, skipped or failed.
    /// </summary>
    public bool IsSettled(int groupId, ExecutionState executionState)
    {
        foreach (var node in NodesByGroup[groupId])
        {
            if (!Finished[node.Id] && !executionState.IsNodeSkipped(node.Id))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Writes the rows of a payload that the delivery claimed for a delivery group below an anchor object.
/// </summary>
internal sealed class DeliveryPayloadDataFormatter(
    CompositeResultDocument document,
    CompositeResultDocument.Cursor anchor,
    int deliveryGroupId) : IRawJsonFormatter
{
    public void WriteDataTo(JsonWriter jsonWriter)
        => document.WriteDeliveryRows(jsonWriter, anchor, deliveryGroupId);
}

/// <summary>
/// The property row offsets of an object block that a request holds back from the initial payload.
/// </summary>
internal sealed class DeliveryBlock(int[] excludedRowOffsets, int[] heldRowOffsets)
{
    public int[] ExcludedRowOffsets { get; } = excludedRowOffsets;

    public int[] HeldRowOffsets { get; } = heldRowOffsets;
}
