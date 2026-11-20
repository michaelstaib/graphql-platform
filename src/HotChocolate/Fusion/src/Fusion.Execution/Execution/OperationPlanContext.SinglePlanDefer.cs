using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Text.Json;
using HotChocolate.Types;

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
            DeliveryGroup.GetActive(plan.DeliveryGroups, Variables),
            ExecutionState);
        _singlePlanDelivery = state;
        _resultStore.Result.AttachDelivery(state);
        _resultStore.EnableDelivery();
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
    /// Hands the errors that were recorded after the first <paramref name="count"/> errors and that no delivery
    /// group owns to <paramref name="deliveryGroups"/>.
    /// </summary>
    internal void AddSourceErrorsAfter(int count, ImmutableArray<int> deliveryGroups)
    {
        var errors = _resultStore.Errors;

        if (errors is null || errors.Count <= count || deliveryGroups.IsDefaultOrEmpty)
        {
            return;
        }

        var state = _singlePlanDelivery!;

        for (var i = count; i < errors.Count; i++)
        {
            if (!state.IsGroupError(errors[i]))
            {
                state.AddSourceError(errors[i], deliveryGroups);
            }
        }
    }

    /// <summary>
    /// Gets the errors of the result that were recorded after the first <paramref name="count"/> errors and
    /// that no delivery group owns.
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
            if (_singlePlanDelivery?.IsGroupError(errors[i]) is not true)
            {
                builder.Add(errors[i]);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Writes the errors that failed steps reported, each under the write filter of its step.
    /// </summary>
    internal void ApplyDeliveryErrors(SinglePlanDeliveryState state)
    {
        while (state.TryDequeueError(out var error))
        {
            var groups = state.GetDeliveryGroups(error.ResultSelectionSet);

            state.AddStepError(error.Error, groups);
            state.AddDependentStepError(error.Error, error.ResultSelectionSet);

            var errorCount = ErrorCount;

            state.AppliesStepErrors = true;

            try
            {
                if (!_resultStore.AddDeliveryErrors(error, groups))
                {
                    ExecutionState.CancelProcessing();
                }
            }
            finally
            {
                state.AppliesStepErrors = false;
            }

            AddSourceErrorsAfter(errorCount, state.GetOwnDeliveryGroups(error.ResultSelectionSet));
        }
    }

    internal CompositeResultDocument ResultDocument => _resultStore.Result;

    internal int ErrorCount => _resultStore.Errors?.Count ?? 0;
}

/// <summary>
/// The per request state of a plan with marked steps: where each field is delivered, which steps are
/// needed and how far each delivery group is.
/// </summary>
internal sealed partial class SinglePlanDeliveryState
{
    private readonly Dictionary<int, SelectionDelivery> _deliveries = [];
    private readonly Dictionary<int, DeliveryBlock> _blocks = [];
    private readonly object _sync = new();
    private readonly List<ExecutionNode> _markedNodes = [];
    private readonly Dictionary<SelectionPath, SelectionSet[]> _targetSets = [];
    private readonly Dictionary<ResultSelectionSet, ImmutableArray<int>> _groupsByResultSelectionSet
        = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ResultSelectionSet, ImmutableArray<int>> _ownGroupsByResultSelectionSet
        = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentQueue<DeliveryError> _errors = new();
    private readonly ExecutionState _executionState;
    private Dictionary<int, List<ExecutionNode>>? _writers;
    private volatile bool _queuesErrors;
#if DEBUG
    private int _loopThreadId;
#endif

    public SinglePlanDeliveryState(
        OperationPlan plan,
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        ConditionFlags activeGroups,
        ExecutionState executionState)
    {
        Plan = plan;
        _executionState = executionState;
        IncludeFlags = includeFlags;
        DeferFlags = deferFlags;
        ActiveGroups = activeGroups;

        var groupCount = 0;

        foreach (var group in plan.DeliveryGroups)
        {
            groupCount = Math.Max(groupCount, group.Id + 1);
        }

        _groupsById = new DeliveryGroup[groupCount];
        _anchorDepths = new int[groupCount];
        _groupStatus = new byte[groupCount];

        foreach (var group in plan.DeliveryGroups)
        {
            _groupsById[group.Id] = group;
            _anchorDepths[group.Id] = CountFieldSegments(group.Path);
        }

        Honored = new bool[groupCount];
        NodesByGroup = new List<ExecutionNode>[groupCount];
        Needed = new bool[plan.MaxNodeId + 1];
        Finished = new bool[plan.MaxNodeId + 1];
        Failed = new bool[plan.MaxNodeId + 1];
        FailureExceptions = new Exception?[plan.MaxNodeId + 1];

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
            AddResultSelectionSets(node);

            foreach (var group in groups)
            {
                NodesByGroup[group].Add(node);
            }
        }
    }

    public OperationPlan Plan { get; }

    /// <summary>
    /// Marks the start of a section in which the calling thread applies results and creates payloads.
    /// </summary>
    [Conditional("DEBUG")]
    public void EnterLoop()
    {
#if DEBUG
        _loopThreadId = Environment.CurrentManagedThreadId;
#endif
    }

    /// <summary>
    /// Marks the end of the section that <see cref="EnterLoop"/> started.
    /// </summary>
    [Conditional("DEBUG")]
    public void ExitLoop()
    {
#if DEBUG
        _loopThreadId = 0;
#endif
    }

    /// <summary>
    /// Asserts that the calling thread is inside a section that <see cref="EnterLoop"/> started.
    /// </summary>
    [Conditional("DEBUG")]
    public void AssertOnLoop()
    {
#if DEBUG
        Debug.Assert(
            _loopThreadId == Environment.CurrentManagedThreadId,
            "The result document is written outside of the loop of the single plan delivery.");
#endif
    }

    /// <summary>
    /// Asserts that no other thread is inside a section that <see cref="EnterLoop"/> started.
    /// </summary>
    [Conditional("DEBUG")]
    public void AssertLoopNotRunningElsewhere()
    {
#if DEBUG
        var owner = _loopThreadId;

        Debug.Assert(
            owner == 0 || owner == Environment.CurrentManagedThreadId,
            "The result document is read while another thread runs the loop of the single plan delivery.");
#endif
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the errors of failed steps are queued for the executor.
    /// </summary>
    public bool QueuesErrors
    {
        get => _queuesErrors;
        set => _queuesErrors = value;
    }

    public ConditionFlags IncludeFlags { get; }

    public ConditionFlags DeferFlags { get; }

    public ConditionFlags ActiveGroups { get; }

    public IReadOnlyList<ExecutionNode> MarkedNodes => _markedNodes;

    public bool[] Honored { get; }

    public List<ExecutionNode>[] NodesByGroup { get; }

    public bool[] Needed { get; }

    public bool[] Finished { get; }

    public bool[] Failed { get; }

    /// <summary>
    /// Gets the exception of each failed step, indexed by step id.
    /// </summary>
    public Exception?[] FailureExceptions { get; }

    /// <summary>
    /// Gets the number of errors that payloads already carry.
    /// </summary>
    public int PublishedErrorCount { get; set; }

    /// <summary>
    /// Queues the errors of a failed step.
    /// </summary>
    public void EnqueueError(IError error, ResultSelectionSet resultSelectionSet, ReadOnlySpan<Path> paths)
        => _errors.Enqueue(new DeliveryError(error, resultSelectionSet, paths.ToArray(), compactPaths: null));

    /// <summary>
    /// Queues the errors of a failed step.
    /// </summary>
    public void EnqueueError(IError error, ResultSelectionSet resultSelectionSet, ReadOnlySpan<CompactPath> paths)
    {
        var detached = new CompactPath[paths.Length];

        for (var i = 0; i < detached.Length; i++)
        {
            detached[i] = paths[i].Detach();
        }

        _errors.Enqueue(new DeliveryError(error, resultSelectionSet, paths: null, detached));
    }

    public bool TryDequeueError([NotNullWhen(true)] out DeliveryError? error)
        => _errors.TryDequeue(out error);

    /// <summary>
    /// Gets the delivery groups of the step that owns <paramref name="resultSelectionSet"/>, or an empty
    /// array when the step belongs to the initial payload.
    /// </summary>
    public ImmutableArray<int> GetDeliveryGroups(ResultSelectionSet resultSelectionSet)
        => _groupsByResultSelectionSet.TryGetValue(resultSelectionSet, out var groups) ? groups : default;

    /// <summary>
    /// Gets the delivery groups of the operation that owns <paramref name="resultSelectionSet"/>, or an empty
    /// array when the operation belongs to the initial payload.
    /// </summary>
    public ImmutableArray<int> GetOwnDeliveryGroups(ResultSelectionSet resultSelectionSet)
        => _ownGroupsByResultSelectionSet.TryGetValue(resultSelectionSet, out var groups) ? groups : default;

    private void AddResultSelectionSets(ExecutionNode node)
    {
        switch (node)
        {
            case OperationExecutionNode operation:
                _groupsByResultSelectionSet[operation.ResultSelectionSet] = operation.DeliveryGroups;
                _ownGroupsByResultSelectionSet[operation.ResultSelectionSet] = operation.DeliveryGroups;
                break;

            case OperationBatchExecutionNode batch:
                var groups = GetDeliveryGroups(batch);

                foreach (var definition in batch.Operations)
                {
                    _groupsByResultSelectionSet[definition.ResultSelectionSet] = groups;
                    _ownGroupsByResultSelectionSet[definition.ResultSelectionSet] = definition.DeliveryGroups;
                }

                break;
        }
    }

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
            var delivery = GetDelivery(selections[i]);

            if (delivery.Kind is DeliveryKind.Initial)
            {
                continue;
            }

            excluded.Add((i * 2) + 1);

            if (delivery.Kind is DeliveryKind.Deferred)
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
    public SelectionDelivery GetDelivery(Selection selection)
    {
        lock (_sync)
        {
            if (!_deliveries.TryGetValue(selection.Id, out var delivery))
            {
                delivery = selection.GetDelivery(IncludeFlags, DeferFlags);
                _deliveries.Add(selection.Id, delivery);
            }

            return delivery;
        }
    }

    /// <summary>
    /// Determines whether a step of the plan reads <paramref name="selection"/> as a key or a requirement.
    /// </summary>
    public bool IsKeyOrRequirement(Selection selection)
        => Plan.KeyAndRequirementSelectionIds.Contains(selection.Id);

    /// <summary>
    /// Determines whether a step that is not settled can still write the row of <paramref name="selection"/>: a
    /// step of the groups of the row, another step whose answer can hold the row as a key or as a requirement, or
    /// a step of a group nested in a group of the row whose answer can hold the row as an object.
    /// </summary>
    public bool HasOpenWriter(Selection selection, SelectionDelivery delivery)
    {
        if (!AreGroupsSettled(delivery))
        {
            return true;
        }

        var isKeyOrRequirement = IsKeyOrRequirement(selection);

        if ((!isKeyOrRequirement && selection.IsLeaf) || !TryGetWriters(selection, out var writers))
        {
            return false;
        }

        foreach (var node in writers)
        {
            if (!Finished[node.Id]
                && !_executionState.IsNodeSkipped(node.Id)
                && (isKeyOrRequirement || IsNestedIn(delivery, GetDeliveryGroups(node))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a failed step of the group left the row of <paramref name="selection"/>: a step whose
    /// answer can hold the row failed, or was skipped for a failed step that it requires.
    /// </summary>
    public bool IsLeftByFailedStep(Selection selection, int groupId)
    {
        if (!TryGetWriters(selection, out var writers))
        {
            return false;
        }

        foreach (var node in writers)
        {
            if (GetDeliveryGroups(node).Contains(groupId)
                && (Failed[node.Id] || IsSkippedForFailedStep(groupId, node)))
            {
                return true;
            }
        }

        return false;
    }

    // The step, or an operation of the batch, was skipped for a failed step that it requires.
    private bool IsSkippedForFailedStep(int groupId, ExecutionNode node)
    {
        if (_dependencyErrors?[groupId] is { } dependencyErrors)
        {
            foreach (var dependencyError in dependencyErrors)
            {
                if (Counts(dependencyError) && IsStepOrOperationOf(node, dependencyError.Dependent))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsStepOrOperationOf(ExecutionNode node, int id)
    {
        if (node.Id == id)
        {
            return true;
        }

        if (node is OperationBatchExecutionNode batch)
        {
            foreach (var definition in batch.Operations)
            {
                if (definition.Id == id)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Gets the marked steps whose answer can hold the row of the selection. A field without a child result
    // selection set can hold every selection below it.
    private bool TryGetWriters(Selection selection, [NotNullWhen(true)] out List<ExecutionNode>? writers)
    {
        if (_writers is null)
        {
            _writers = [];

            foreach (var node in _markedNodes)
            {
                switch (node)
                {
                    case OperationExecutionNode operation:
                        AddWriters(node, operation.Target, operation.ResultSelectionSet);
                        break;

                    case OperationBatchExecutionNode batch:
                        foreach (var definition in batch.Operations)
                        {
                            switch (definition)
                            {
                                case SingleOperationDefinition single:
                                    AddWriters(node, single.Target, single.ResultSelectionSet);
                                    break;

                                case BatchOperationDefinition many:
                                    foreach (var target in many.Targets)
                                    {
                                        AddWriters(node, target, many.ResultSelectionSet);
                                    }

                                    break;
                            }
                        }

                        break;
                }
            }
        }

        return _writers.TryGetValue(selection.Id, out writers);
    }

    private void AddWriters(ExecutionNode node, SelectionPath target, ResultSelectionSet resultSelectionSet)
    {
        foreach (var selectionSet in GetTargetSelectionSets(target))
        {
            AddWriters(node, selectionSet, resultSelectionSet);
        }
    }

    private void AddWriters(ExecutionNode node, SelectionSet selectionSet, ResultSelectionSet? resultSelectionSet)
    {
        if (resultSelectionSet is null)
        {
            foreach (var selection in selectionSet.Selections)
            {
                AddWriter(node, selection, child: null);
            }

            return;
        }

        foreach (var written in resultSelectionSet.Selections)
        {
            if (selectionSet.TryGetSelection(written.ResponseName, out var selection))
            {
                AddWriter(node, selection, written.Child);
            }
        }

        foreach (var fragment in resultSelectionSet.Fragments)
        {
            AddWriters(node, selectionSet, fragment.Body);
        }
    }

    private void AddWriter(ExecutionNode node, Selection selection, ResultSelectionSet? child)
    {
        if (!_writers!.TryGetValue(selection.Id, out var writers))
        {
            writers = _writers[selection.Id] = [];
        }

        if (!writers.Contains(node))
        {
            writers.Add(node);
        }

        if (selection.IsLeaf)
        {
            return;
        }

        foreach (var type in Plan.Operation.GetPossibleTypes(selection))
        {
            AddWriters(node, Plan.Operation.GetSelectionSet(selection, type), child);
        }
    }

    /// <summary>
    /// Collects the ids of the selections that a step of the plan can read as a key or a requirement.
    /// The result can contain selections that no step reads.
    /// </summary>
    internal static HashSet<int> CollectKeyAndRequirementSelectionIds(
        Operation operation,
        ImmutableArray<ExecutionNode> nodes)
    {
        var ids = new HashSet<int>();

        foreach (var node in nodes)
        {
            switch (node)
            {
                case OperationExecutionNode single:
                    CollectKeyAndRequirementSelectionIds(operation, single.Requirements, ids);
                    break;

                case OperationBatchExecutionNode batch:
                    foreach (var definition in batch.Operations)
                    {
                        CollectKeyAndRequirementSelectionIds(operation, definition.Requirements, ids);
                    }

                    break;
            }
        }

        return ids;
    }

    private static void CollectKeyAndRequirementSelectionIds(
        Operation operation,
        ReadOnlySpan<OperationRequirement> requirements,
        HashSet<int> ids)
    {
        foreach (var requirement in requirements)
        {
            if (requirement.InternalAlias is not null)
            {
                foreach (var selectionSet in GetTargetSelectionSets(operation, requirement.Path))
                {
                    if (!TryGetAliasedSelection(requirement, selectionSet, out var aliased))
                    {
                        continue;
                    }

                    ids.Add(aliased.Id);

                    if (!aliased.IsLeaf)
                    {
                        CollectKeyAndRequirementSelectionIds(operation, aliased, GetRequirementNames(requirement), ids);
                    }
                }

                continue;
            }

            var names = GetRequirementNames(requirement);

            foreach (var selectionSet in GetTargetSelectionSets(operation, requirement.Path))
            {
                CollectKeyAndRequirementSelectionIds(operation, selectionSet, names, ids);
            }
        }
    }

    // Gets the selection that holds the value of a requirement with an internal alias. Such a requirement is written
    // under the alias and not under the name of its field.
    private static bool TryGetAliasedSelection(
        OperationRequirement requirement,
        SelectionSet selectionSet,
        [NotNullWhen(true)] out Selection? selection)
    {
        if (requirement.InternalAlias is { } internalAlias)
        {
            return selectionSet.TryGetSelection(internalAlias, out selection);
        }

        selection = null;
        return false;
    }

    private static HashSet<string> GetRequirementNames(OperationRequirement requirement)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var map = requirement.Map.ToString() ?? string.Empty;
        var start = -1;

        for (var i = 0; i <= map.Length; i++)
        {
            var isNameChar = i < map.Length && (char.IsLetterOrDigit(map[i]) || map[i] == '_');

            if (isNameChar && start < 0)
            {
                start = i;
            }
            else if (!isNameChar && start >= 0)
            {
                names.Add(map[start..i]);
                start = -1;
            }
        }

        return names;
    }

    private static void CollectKeyAndRequirementSelectionIds(
        Operation operation,
        SelectionSet selectionSet,
        HashSet<string> names,
        HashSet<int> ids)
    {
        foreach (var name in names)
        {
            if (!selectionSet.TryGetSelection(name, out var selection))
            {
                continue;
            }

            ids.Add(selection.Id);

            if (!selection.IsLeaf)
            {
                CollectKeyAndRequirementSelectionIds(operation, selection, names, ids);
            }
        }
    }

    private static void CollectKeyAndRequirementSelectionIds(
        Operation operation,
        Selection selection,
        HashSet<string> names,
        HashSet<int> ids)
    {
        foreach (var type in operation.GetPossibleTypes(selection))
        {
            CollectKeyAndRequirementSelectionIds(operation, operation.GetSelectionSet(selection, type), names, ids);
        }
    }

    public static bool Contains(SelectionDelivery delivery, int groupId)
    {
        foreach (var group in delivery.Groups)
        {
            if (group.Id == groupId)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Intersects(SelectionDelivery delivery, ImmutableArray<int> groupIds)
    {
        foreach (var group in delivery.Groups)
        {
            if (groupIds.Contains(group.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a group of <paramref name="groupIds"/> is nested in a group of
    /// <paramref name="delivery"/>.
    /// </summary>
    public bool IsNestedIn(SelectionDelivery delivery, ImmutableArray<int> groupIds)
    {
        foreach (var groupId in groupIds)
        {
            for (var ancestor = _groupsById[groupId].Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (Contains(delivery, ancestor.Id))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Decides which marked steps run for this request and which delivery groups are honored.
    /// A step is needed when one of its top level fields resolves to one of its groups, or when a needed step or
    /// a needed operation of a batch depends on it. A group is honored when it is active and a needed step
    /// delivers a field to it or a selection of the operation resolves to it.
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

                if (node is OperationExecutionNode step && MarkNeededProviders(step))
                {
                    changed = true;
                }

                if (node is OperationBatchExecutionNode batch)
                {
                    foreach (var definition in batch.Operations)
                    {
                        if (Needed[definition.Id] && MarkNeededDependencies(definition))
                        {
                            changed = true;
                        }
                    }
                }
            }
        }

        HonorGroupsOfResolvedSelections();
        ComputeEffectiveChildren();
    }

    // Honors the active groups that a selection of the operation resolves to, also when no step of the group is
    // needed.
    private void HonorGroupsOfResolvedSelections()
    {
        foreach (var group in Plan.DeliveryGroups)
        {
            if (!Honored[group.Id]
                && ActiveGroups.IsSet(group.Id)
                && IsHonoredAt(group.Id, Plan.Operation.RootSelectionSet))
            {
                Honored[group.Id] = true;
            }
        }
    }

    // Marks the steps and operations that the operation takes a key from as needed. Returns whether a flag changed.
    private bool MarkNeededDependencies(OperationDefinition definition)
    {
        var marked = false;

        foreach (var dependency in definition.Dependencies)
        {
            var dependencyNode = Plan.GetExecutionNode(dependency);

            if (Needed[dependency.Id] && Needed[dependencyNode.Id])
            {
                continue;
            }

            if (GetDeliveryGroups(dependencyNode).IsDefaultOrEmpty)
            {
                continue;
            }

            Needed[dependency.Id] = true;
            Needed[dependencyNode.Id] = true;
            marked = true;
        }

        return marked;
    }

    // Marks the operations of a batch that the step depends on and that write a key or requirement of the step as
    // needed. Returns whether a flag changed.
    private bool MarkNeededProviders(OperationExecutionNode step)
    {
        var marked = false;

        foreach (var dependency in step.Dependencies)
        {
            if (Plan.GetExecutionNode(dependency) is not OperationBatchExecutionNode provider
                || GetDeliveryGroups(provider).IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var definition in provider.Operations)
            {
                if (!Needed[definition.Id]
                    && IsInActiveGroup(definition)
                    && Supplies(definition, step.Requirements))
                {
                    Needed[definition.Id] = true;
                    marked = true;
                }
            }
        }

        return marked;
    }

    private bool IsInActiveGroup(OperationDefinition definition)
    {
        if (definition.DeliveryGroups.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var groupId in definition.DeliveryGroups)
        {
            if (ActiveGroups.IsSet(groupId))
            {
                return true;
            }
        }

        return false;
    }

    private bool Supplies(OperationDefinition definition, ReadOnlySpan<OperationRequirement> requirements)
    {
        foreach (var requirement in requirements)
        {
            if (requirement.InternalAlias is not null)
            {
                foreach (var selectionSet in GetTargetSelectionSets(requirement.Path))
                {
                    if (TryGetAliasedSelection(requirement, selectionSet, out var key) && Writes(definition, key))
                    {
                        return true;
                    }
                }

                continue;
            }

            var names = GetRequirementNames(requirement);

            foreach (var selectionSet in GetTargetSelectionSets(requirement.Path))
            {
                foreach (var name in names)
                {
                    if (selectionSet.TryGetSelection(name, out var key) && Writes(definition, key))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool Writes(OperationDefinition definition, Selection selection)
    {
        switch (definition)
        {
            case SingleOperationDefinition single:
                return Writes(single.Target, single.ResultSelectionSet, selection);

            case BatchOperationDefinition many:
                foreach (var target in many.Targets)
                {
                    if (Writes(target, many.ResultSelectionSet, selection))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private bool Writes(SelectionPath target, ResultSelectionSet resultSelectionSet, Selection selection)
    {
        if (!resultSelectionSet.ResponseNames.Contains(selection.ResponseName))
        {
            return false;
        }

        foreach (var selectionSet in GetTargetSelectionSets(target))
        {
            if (selectionSet.TryGetSelection(selection.ResponseName, out var written) && written.Id == selection.Id)
            {
                return true;
            }
        }

        return false;
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
                List<int>? own = null;

                foreach (var definition in batch.Operations)
                {
                    own ??= [];
                    own.Clear();

                    switch (definition)
                    {
                        case SingleOperationDefinition single:
                            CollectHonored(
                                single.Target,
                                single.ResultSelectionSet,
                                definition.DeliveryGroups,
                                own);
                            break;

                        case BatchOperationDefinition many:
                            foreach (var target in many.Targets)
                            {
                                CollectHonored(target, many.ResultSelectionSet, definition.DeliveryGroups, own);
                            }

                            break;
                    }

                    if (own.Count == 0)
                    {
                        continue;
                    }

                    // An operation that delivers to an active group of its own is needed itself.
                    Needed[definition.Id] = true;

                    foreach (var group in own)
                    {
                        if (!honored.Contains(group))
                        {
                            honored.Add(group);
                        }
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

            foreach (var group in GetDelivery(field).Groups)
            {
                if (groups.Contains(group.Id) && ActiveGroups.IsSet(group.Id) && !honored.Contains(group.Id))
                {
                    honored.Add(group.Id);
                }
            }

            if (!field.IsLeaf)
            {
                CollectHonoredBelow(field, groups, honored);
            }
        }

        foreach (var fragment in resultSelectionSet.Fragments)
        {
            CollectHonored(selectionSet, fragment.Body, groups, honored);
        }
    }

    private void CollectHonoredBelow(Selection field, ImmutableArray<int> groups, List<int> honored)
    {
        foreach (var type in Plan.Operation.GetPossibleTypes(field))
        {
            foreach (var child in Plan.Operation.GetSelectionSet(field, type).Selections)
            {
                foreach (var group in GetDelivery(child).Groups)
                {
                    if (groups.Contains(group.Id) && ActiveGroups.IsSet(group.Id) && !honored.Contains(group.Id))
                    {
                        honored.Add(group.Id);
                    }
                }

                if (!child.IsLeaf)
                {
                    CollectHonoredBelow(child, groups, honored);
                }
            }
        }
    }

    private SelectionSet[] GetTargetSelectionSets(SelectionPath path)
    {
        if (_targetSets.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var result = GetTargetSelectionSets(Plan.Operation, path);
        _targetSets[path] = result;
        return result;
    }

    private static SelectionSet[] GetTargetSelectionSets(Operation operation, SelectionPath path)
    {
        var current = new List<SelectionSet> { operation.RootSelectionSet };
        var selections = new List<Selection>();
        var followsTypeSegment = false;

        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            if (segment.Kind is SelectionPathSegmentKind.Root)
            {
                continue;
            }

            if (segment.Kind is SelectionPathSegmentKind.InlineFragment)
            {
                // A type segment keeps the selection sets of the object types that are its type or assignable to it.
                var segmentType = operation.Schema.Types.GetType<ITypeDefinition>(segment.Name);

                if (followsTypeSegment)
                {
                    KeepSelectionSetsOfType(current, segmentType);
                    continue;
                }

                followsTypeSegment = true;
                current.Clear();

                foreach (var selection in selections)
                {
                    foreach (var type in operation.GetPossibleTypes(selection))
                    {
                        if (segmentType.IsAssignableFrom(type))
                        {
                            current.Add(operation.GetSelectionSet(selection, type));
                        }
                    }
                }

                selections.Clear();
                continue;
            }

            followsTypeSegment = false;

            if (selections.Count > 0)
            {
                ExpandPossibleTypes(operation, selections, current);
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
            ExpandPossibleTypes(operation, selections, current);
        }

        return current.ToArray();
    }

    private static void KeepSelectionSetsOfType(List<SelectionSet> selectionSets, ITypeDefinition segmentType)
    {
        var kept = 0;

        for (var i = 0; i < selectionSets.Count; i++)
        {
            if (segmentType.IsAssignableFrom(selectionSets[i].Type))
            {
                selectionSets[kept++] = selectionSets[i];
            }
        }

        selectionSets.RemoveRange(kept, selectionSets.Count - kept);
    }

    private static void ExpandPossibleTypes(Operation operation, List<Selection> selections, List<SelectionSet> output)
    {
        output.Clear();

        foreach (var selection in selections)
        {
            foreach (var type in operation.GetPossibleTypes(selection))
            {
                output.Add(operation.GetSelectionSet(selection, type));
            }
        }
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
/// The error of a failed step with the result paths it applies to.
/// </summary>
internal sealed class DeliveryError(
    IError error,
    ResultSelectionSet resultSelectionSet,
    Path[]? paths,
    CompactPath[]? compactPaths)
{
    public IError Error { get; } = error;

    public ResultSelectionSet ResultSelectionSet { get; } = resultSelectionSet;

    public Path[]? Paths { get; } = paths;

    public CompactPath[]? CompactPaths { get; } = compactPaths;
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
