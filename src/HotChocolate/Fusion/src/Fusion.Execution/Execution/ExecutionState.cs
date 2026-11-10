using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution;

internal sealed class ExecutionState
{
    private const byte NodeStateNone = 0;
    private const byte NodeStateBacklog = 1;
    private const byte NodeStateSkipped = 2;

    private readonly List<ExecutionNode> _stack = [];
    private readonly List<ExecutionNode> _ready = [];
    private readonly List<int> _trackedNodeStateSlots = [];
    private readonly List<int> _trackedDependencySlots = [];
    private readonly ConcurrentQueue<ExecutionNodeResult> _completedResults = new();
    private readonly ConcurrentQueue<PendingMerge> _pendingMerges = new();
    private Dictionary<int, Exception?>? _mergeFailures;
    private ulong[] _failedOrSkippedBitset = [];
    private ulong[] _completingSkipBitset = [];
    private ulong[] _nonCompletingSkipBitset = [];
    private bool _hasCompletingSkip;

    private bool _collectTelemetry;
    private bool _processingCompletedEarly;
    private CancellationTokenSource _cts = default!;
    private byte[] _nodeStates = [];
    private int[] _remainingDependencies = [];
    private int _backlogCount;
    private int _activeNodes;

    public readonly OrderedDictionary<int, ExecutionNodeTrace> Traces = [];
    public readonly AsyncAutoResetEvent Signal = new();

    public void Initialize(bool collectTelemetry, CancellationTokenSource cts)
    {
        _collectTelemetry = collectTelemetry;
        _cts = cts;
    }

    /// <summary>
    /// Sets the CancellationTokenSource that <see cref="CancelProcessing"/> cancels, letting a subscription scope it
    /// per event instead of to the whole request.
    /// </summary>
    public void SetCancellationSource(CancellationTokenSource cts)
        => _cts = cts;

    /// <summary>
    /// True when processing stopped early because a field error null-propagated to the root,
    /// settling the result as <c>null</c>. Lets the caller tell this self-inflicted stop from a real
    /// cancellation (timeout or abort).
    /// </summary>
    public bool ProcessingCompletedEarly => _processingCompletedEarly;

    public void Clean()
    {
        Reset();
        _cts = default!;
    }

    public void Destroy()
    {
        if (_nodeStates.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_nodeStates);
            _nodeStates = [];
        }

        if (_remainingDependencies.Length > 0)
        {
            ArrayPool<int>.Shared.Return(_remainingDependencies);
            _remainingDependencies = [];
        }

        ReturnBitset(ref _failedOrSkippedBitset);
        ReturnBitset(ref _completingSkipBitset);
        ReturnBitset(ref _nonCompletingSkipBitset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsNodeSkipped(int nodeId)
    {
        var index = nodeId >> 6;
        return index < _failedOrSkippedBitset.Length
            && (_failedOrSkippedBitset[index] & (1UL << (nodeId & 63))) != 0;
    }

    public void FillBacklog(IOperationPlan plan)
    {
        _ready.Clear();
        _backlogCount = 0;
        ClearFailedOrSkippedBitset();

        ResetNodeStates();
        ResetRemainingDependencies();

        switch (plan.Operation.Definition.Operation)
        {
            case OperationType.Query:
                foreach (var node in plan.AllNodes)
                {
                    AddToBacklog(node);
                }
                break;

            case OperationType.Mutation:
                foreach (var node in plan.AllNodes)
                {
                    // we skip root nodes as they are enqueued by the algorithm
                    // one by one.
                    if (node.Dependencies.Length == 0 && node.OptionalDependencies.Length == 0)
                    {
                        continue;
                    }

                    AddToBacklog(node);
                }
                break;

            case OperationType.Subscription:
                var root = plan.RootNodes.Single();

                foreach (var node in plan.AllNodes)
                {
                    if (!ReferenceEquals(node, root))
                    {
                        AddToBacklog(node);
                    }
                }

                // The root node of a subscription is started outside the state.
                // We cater to this fact and fix the state by stating with am active node count of 1.
                Interlocked.Increment(ref _activeNodes);
                break;

            default:
                throw new ArgumentOutOfRangeException("Unexpected operation type.");
        }
    }

    public void Reset()
    {
        _stack.Clear();
        _ready.Clear();
        _backlogCount = 0;
        ClearFailedOrSkippedBitset();

        ResetNodeStates();
        ResetRemainingDependencies();

        while (_completedResults.TryDequeue(out _))
        {
            // do nothing, just clear the queue
        }

        ClearPendingMerges();
        _mergeFailures?.Clear();
        _activeNodes = 0;
        _processingCompletedEarly = false;

        Traces.Clear();
        Signal.TryResetToIdle();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsProcessing() => _backlogCount > 0 || Volatile.Read(ref _activeNodes) > 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasActiveNodes() => Volatile.Read(ref _activeNodes) > 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void StartNode(OperationPlanContext context, ExecutionNode node, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeNodes);

        if (node.Id < _remainingDependencies.Length)
        {
            _remainingDependencies[node.Id] = -1;
        }

        RemoveFromBacklog(node.Id, NodeStateNone);
        node.BeginExecute(context, cancellationToken);
    }

    public void EnqueueForCompletion(ExecutionNodeResult result)
    {
        _completedResults.Enqueue(result);
        Signal.Set();
    }

    public bool TryDequeueCompletedResult(out ExecutionNodeResult result)
        => _completedResults.TryDequeue(out result);

    public void EnqueueMerge(PendingMerge merge)
    {
        _pendingMerges.Enqueue(merge);
        Signal.Set();
    }

    public bool TryDequeuePendingMerge(out PendingMerge merge)
        => _pendingMerges.TryDequeue(out merge);

    public void ApplyMerge(OperationPlanContext context, PendingMerge merge)
    {
        try
        {
            merge.Apply(context);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            MarkMergeFailed(merge.Node.Id, exception: null);
        }
        catch (Exception exception)
        {
            MarkMergeFailed(merge.Node.Id, exception);
            context.DiagnosticEvents.SourceSchemaStoreError(
                context,
                merge.Node,
                merge.SchemaName,
                exception);
            merge.AddErrors(context, exception);
        }
    }

    public ExecutionNodeResult ApplyPendingMergeFailure(ExecutionNodeResult result)
    {
        if (_mergeFailures is not null
            && _mergeFailures.Remove(result.Id, out var exception))
        {
            return result with
            {
                Status = ExecutionStatus.Failed,
                Exception = exception
            };
        }

        return result;
    }

    public void CancelProcessing()
    {
        _processingCompletedEarly = true;

        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    public void CompleteNode(
        IOperationPlan plan,
        ExecutionNode node,
        ExecutionNodeResult result)
    {
        Interlocked.Decrement(ref _activeNodes);

        if (_collectTelemetry)
        {
            Traces.TryAdd(
                result.Id,
                new ExecutionNodeTrace
                {
                    Id = result.Id,
                    SpanId = result.Activity?.SpanId.ToHexString(),
                    Status = result.Status,
                    Duration = result.Duration,
                    VariableSets = result.VariableValueSets,
                    Transport = result.TransportDetails.Uri is not null
                        && result.TransportDetails.ContentType is not null
                            ? new ExecutionNodeTransportTrace
                            {
                                Uri = result.TransportDetails.Uri,
                                ContentType = result.TransportDetails.ContentType
                            }
                            : null
                });
        }

        // When a batch node executes it may skip some of its individual operations
        // because their specific dependencies failed. We record those operation
        // definition identifiers here so that downstream nodes that depend on a
        // particular operation inside the batch can see that it was skipped.
        if (!result.SkippedDefinitions.IsDefaultOrEmpty)
        {
            foreach (var def in result.SkippedDefinitions)
            {
                MarkNodeAsSkipped(def.Id);
            }

            RecordSkippedDefinitions(result);
        }

        if (result.Status is ExecutionStatus.Success or ExecutionStatus.PartialSuccess)
        {
            // a node can explicitly choose which of its dependents should run
            // by calling EnqueueDependentForExecution during execution.
            // if it did, any dependent not in that list is skipped.
            if (!result.DependentsToExecute.IsDefault)
            {
                var dependentsToExecute = result.DependentsToExecute;

                foreach (var dependent in node.Dependents)
                {
                    var executionDependent = plan.GetExecutionNode(dependent);

                    if (!ContainsDependent(dependentsToExecute, executionDependent))
                    {
                        SkipNode(plan, executionDependent, SkipKind.NonCompleting);
                    }
                }
            }

            // decrement the remaining dependency count for each dependent.
            // when a dependent's count reaches 0 all its dependencies are
            // fulfilled and it is ready to execute.
            foreach (var dependent in node.Dependents)
            {
                // When the dependent is an operation definition inside a batch,
                // there is no backlog entry to update. The batch node's own
                // execution-level dependencies handle its scheduling. We just
                // need to track the remaining dependency count for execution nodes.
                var executionNode = plan.GetExecutionNode(dependent);
                if (executionNode.Id >= _remainingDependencies.Length)
                {
                    continue;
                }

                var remainingDependencies = _remainingDependencies[executionNode.Id];
                if (remainingDependencies <= 0)
                {
                    continue;
                }

                if (remainingDependencies == 1)
                {
                    _remainingDependencies[executionNode.Id] = 0;
                    _ready.Add(executionNode);
                }
                else if (remainingDependencies > 1)
                {
                    _remainingDependencies[executionNode.Id] = remainingDependencies - 1;
                }
            }
        }

        if (result.Status is ExecutionStatus.Skipped or ExecutionStatus.Failed)
        {
            SkipNode(plan, node, GetSkipKind(result));
        }
    }

    // A node that failed or skipped itself after it ran completes its dependents. A node skipped by its own
    // condition does not, and a cancelled node is neither.
    private SkipKind GetSkipKind(ExecutionNodeResult result)
    {
        if (_cts is { IsCancellationRequested: true })
        {
            return SkipKind.Cancelled;
        }

        return result.Status is ExecutionStatus.Skipped && result.SkippedByCondition
            ? SkipKind.NonCompleting
            : SkipKind.Completing;
    }

    /// <summary>
    /// Gets whether a node was skipped without running because a dependency failed or was skipped, and no skip
    /// reached it that does not complete its dependent fields.
    /// </summary>
    public bool HasCompletingSkips => _hasCompletingSkip;

    /// <summary>
    /// Forgets the completing skips that a pass has consumed.
    /// </summary>
    public void ClearCompletingSkips()
    {
        if (_completingSkipBitset.Length > 0)
        {
            _completingSkipBitset.AsSpan().Clear();
        }

        _hasCompletingSkip = false;
    }

    /// <summary>
    /// Gets whether <paramref name="nodeId"/> is a node, or an operation definition inside a batch, that was skipped
    /// without running, and only skips that complete its dependent fields reached it.
    /// </summary>
    public bool IsCompletingSkip(IOperationPlan plan, int nodeId)
    {
        var index = nodeId >> 6;

        if (index >= _completingSkipBitset.Length)
        {
            return false;
        }

        var mask = 1UL << (nodeId & 63);

        if ((_completingSkipBitset[index] & mask) == 0
            || (index < _nonCompletingSkipBitset.Length && (_nonCompletingSkipBitset[index] & mask) != 0))
        {
            return false;
        }

        // Operation definitions inside a batch have no node state, only execution nodes can have run.
        return plan.GetNodeById(nodeId).Id != nodeId || WasSkippedWithoutRunning(nodeId);
    }

    /// <summary>
    /// Appends the ids of the nodes for which <see cref="IsCompletingSkip"/> is <c>true</c> to
    /// <paramref name="nodeIds"/>, growing the pooled array when needed.
    /// </summary>
    /// <returns>
    /// The number of ids in <paramref name="nodeIds"/>.
    /// </returns>
    public int AppendCompletingSkippedNodes(IOperationPlan plan, ref int[] nodeIds, int count)
    {
        for (var word = 0; word < _completingSkipBitset.Length; word++)
        {
            var bits = _completingSkipBitset[word];

            while (bits != 0)
            {
                var nodeId = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;

                if (!IsCompletingSkip(plan, nodeId))
                {
                    continue;
                }

                if (count == nodeIds.Length)
                {
                    var grown = ArrayPool<int>.Shared.Rent(count * 2);
                    nodeIds.AsSpan(0, count).CopyTo(grown);
                    ArrayPool<int>.Shared.Return(nodeIds);
                    nodeIds = grown;
                }

                nodeIds[count++] = nodeId;
            }
        }

        return count;
    }

    internal void SkipNode(IOperationPlan plan, ExecutionNode node, SkipKind kind)
    {
        _stack.Clear();
        _stack.Push(node);

        while (_stack.TryPop(out var current))
        {
            var isOrigin = ReferenceEquals(current, node);
            MarkNodeAsSkipped(current.Id);
            RecordSkip(current.Id, kind, isOrigin);

            // When a batch node is skipped without executing, every operation
            // definition inside it is also skipped. We mark each of their
            // identifiers so that downstream nodes that depend on a specific
            // operation inside the batch will see it as skipped.
            if (current is OperationBatchExecutionNode batchNode)
            {
                foreach (var op in batchNode.Operations)
                {
                    MarkNodeAsSkipped(op.Id);
                    RecordSkip(op.Id, kind, isOrigin);
                }
            }

            if (current is ApolloOperationBatchExecutionNode apolloBatchNode)
            {
                foreach (var op in apolloBatchNode.Operations)
                {
                    MarkNodeAsSkipped(op.Id);
                    RecordSkip(op.Id, kind, isOrigin);
                }
            }

            if (current.Id < _remainingDependencies.Length)
            {
                _remainingDependencies[current.Id] = -1;
            }

            if (RemoveFromBacklog(current.Id, NodeStateSkipped)
                && _collectTelemetry
                && !Traces.ContainsKey(current.Id))
            {
                Traces.Add(
                    current.Id,
                    new ExecutionNodeTrace
                    {
                        Id = current.Id,
                        Status = ExecutionStatus.Skipped,
                        Duration = TimeSpan.Zero,
                        VariableSets = []
                    });
            }

            foreach (var dependent in current.Dependents)
            {
                // When the dependent is an operation definition inside a batch,
                // we mark it as skipped so that the batch node can check each
                // operation's dependencies during execution and skip the ones
                // whose dependencies failed.
                if (dependent is not ExecutionNode)
                {
                    MarkNodeAsSkipped(dependent.Id);
                    RecordSkip(dependent.Id, kind, isOrigin: false);
                    continue;
                }

                var dependentNode = plan.GetExecutionNode(dependent);

                if (dependentNode.Id >= _remainingDependencies.Length)
                {
                    continue;
                }

                if (_remainingDependencies[dependentNode.Id] < 0)
                {
                    // A node that another skip already reached records this reason as well and passes it on,
                    // so that whether its dependent fields are completed does not depend on the order of the skips.
                    if (WasSkippedWithoutRunning(dependentNode.Id)
                        && RecordSkip(dependentNode.Id, kind, isOrigin: false))
                    {
                        _stack.Push(dependentNode);
                    }

                    continue;
                }

                if (!IsInBacklog(dependentNode.Id))
                {
                    continue;
                }

                // Fast path: no optional dependencies, use existing behavior.
                if (dependentNode.OptionalDependencies.Length == 0)
                {
                    _stack.Push(dependentNode);
                    continue;
                }

                // Check if the failed node is an optional dependency of the dependent.
                if (IsOptionalDependency(dependentNode, current))
                {
                    // Optional dependency failed: decrement counter but don't cascade skip.
                    // The dependent keeps the reason in case every optional dependency fails.
                    RecordSkip(dependentNode.Id, kind, isOrigin: false);
                    var remaining = _remainingDependencies[dependentNode.Id];

                    if (remaining == 1)
                    {
                        _remainingDependencies[dependentNode.Id] = 0;

                        // All deps resolved. If the node has no required deps and all
                        // optional deps failed, skip it (nothing useful to execute).
                        if (ShouldSkipDueToAllOptionalDepsFailed(dependentNode))
                        {
                            _stack.Push(dependentNode);
                        }
                        else
                        {
                            _ready.Add(dependentNode);
                        }
                    }
                    else if (remaining > 1)
                    {
                        _remainingDependencies[dependentNode.Id] = remaining - 1;
                    }
                }
                else
                {
                    // Required dependency failed: cascade skip (existing behavior).
                    _stack.Push(dependentNode);
                }
            }
        }
    }

    // A definition skipped by its own condition is a non-completing skip. A definition skipped because a dependency
    // was skipped takes the reasons of its dependencies, a dependency that failed or skipped itself after it ran
    // completes. A definition that ran and had no variables is not recorded, its missing targets were recorded when
    // its variables were created.
    private void RecordSkippedDefinitions(ExecutionNodeResult result)
    {
        if (_cts is { IsCancellationRequested: true })
        {
            return;
        }

        foreach (var def in result.ConditionSkippedDefinitions)
        {
            RecordSkip(def.Id, SkipKind.NonCompleting, isOrigin: false);
        }

        foreach (var def in result.SkippedDefinitions)
        {
            if (result.ConditionSkippedDefinitions.Contains(def))
            {
                continue;
            }

            RecordSkippedDependencies(def, def.Dependencies);
            RecordSkippedDependencies(def, def.OptionalDependencies);
        }
    }

    private void RecordSkippedDependencies(IOperationPlanNode def, ReadOnlySpan<IOperationPlanNode> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            if (!IsNodeSkipped(dependency.Id))
            {
                continue;
            }

            var kind = HasBit(_nonCompletingSkipBitset, dependency.Id)
                ? SkipKind.NonCompleting
                : SkipKind.Completing;
            RecordSkip(def.Id, kind, isOrigin: false);
        }
    }

    private static bool HasBit(ulong[] bitset, int nodeId)
    {
        var index = nodeId >> 6;
        return index < bitset.Length && (bitset[index] & (1UL << (nodeId & 63))) != 0;
    }

    // Records why a node was skipped. A node that ran and then failed or skipped itself is the origin of a
    // completing skip, its own dependent fields are not completed by it.
    private bool RecordSkip(int nodeId, SkipKind kind, bool isOrigin)
    {
        switch (kind)
        {
            case SkipKind.NonCompleting:
                return SetBit(ref _nonCompletingSkipBitset, nodeId);

            case SkipKind.Completing when !isOrigin:
                _hasCompletingSkip = true;
                return SetBit(ref _completingSkipBitset, nodeId);

            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WasSkippedWithoutRunning(int nodeId)
        => nodeId < _nodeStates.Length && _nodeStates[nodeId] == NodeStateSkipped;

    private bool ShouldSkipDueToAllOptionalDepsFailed(ExecutionNode node)
    {
        // If the node has any required dependencies, it should not be skipped here.
        // Required deps that failed would have already cascaded a skip; if we reach
        // this point the required deps must have succeeded.
        if (node.Dependencies.Length > 0)
        {
            return false;
        }

        // All dependencies are optional. Check if every one of them failed or was skipped.
        foreach (var optDep in node.OptionalDependencies)
        {
            if (!IsNodeSkipped(optDep.Id))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOptionalDependency(ExecutionNode dependent, ExecutionNode dependency)
    {
        foreach (var optDep in dependent.OptionalDependencies)
        {
            if (ReferenceEquals(optDep, dependency))
            {
                return true;
            }
        }

        return false;
    }

    public bool EnqueueNextNodes(OperationPlanContext context, CancellationToken cancellationToken)
    {
        if (_ready.Count == 0)
        {
            return false;
        }

        var isSorted = true;
        var previousId = int.MinValue;
        var readyCount = _ready.Count;

        foreach (var node in _ready)
        {
            if (node.Id < _remainingDependencies.Length
                && _remainingDependencies[node.Id] == 0)
            {
                if (node.Id < previousId)
                {
                    isSorted = false;
                }

                previousId = node.Id;
            }
        }

        if (isSorted)
        {
            var enqueuedAny = false;

            for (var i = 0; i < readyCount; i++)
            {
                var node = _ready[i];

                if (node.Id < _remainingDependencies.Length
                    && _remainingDependencies[node.Id] == 0)
                {
                    StartNode(context, node, cancellationToken);
                    enqueuedAny = true;
                }
            }

            _ready.Clear();
            return enqueuedAny;
        }

        _stack.Clear();

        for (var i = 0; i < readyCount; i++)
        {
            var node = _ready[i];

            if (node.Id < _remainingDependencies.Length
                && _remainingDependencies[node.Id] == 0)
            {
                _stack.Push(node);
            }
        }

        _ready.Clear();

        if (_stack.Count == 0)
        {
            return false;
        }

        if (_stack.Count > 1)
        {
            _stack.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        }

        foreach (var node in _stack)
        {
            StartNode(context, node, cancellationToken);
        }

        return true;
    }

    private void EnsureDependencyCapacity(int minCapacity)
    {
        if (_remainingDependencies.Length >= minCapacity)
        {
            return;
        }

        var newCapacity = _remainingDependencies.Length == 0 ? 8 : _remainingDependencies.Length;

        while (newCapacity < minCapacity)
        {
            newCapacity *= 2;
        }

        var dependencies = ArrayPool<int>.Shared.Rent(newCapacity);
        dependencies.AsSpan().Fill(-1);

        if (_remainingDependencies.Length > 0)
        {
            Array.Copy(_remainingDependencies, dependencies, _remainingDependencies.Length);
            ArrayPool<int>.Shared.Return(_remainingDependencies);
        }

        _remainingDependencies = dependencies;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsInBacklog(int nodeId)
        => nodeId < _nodeStates.Length
            && _nodeStates[nodeId] == NodeStateBacklog;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsDependent(
        ImmutableArray<ExecutionNode> dependentsToExecute,
        ExecutionNode dependent)
    {
        return dependentsToExecute.Length switch
        {
            1 => ReferenceEquals(dependentsToExecute[0], dependent),
            2 => ReferenceEquals(dependentsToExecute[0], dependent)
                || ReferenceEquals(dependentsToExecute[1], dependent),
            3 => ReferenceEquals(dependentsToExecute[0], dependent)
                || ReferenceEquals(dependentsToExecute[1], dependent)
                || ReferenceEquals(dependentsToExecute[2], dependent),
            _ => dependentsToExecute.Contains(dependent)
        };
    }

    internal void AddToBacklog(ExecutionNode node)
    {
        var nodeId = node.Id;

        if (nodeId >= _nodeStates.Length)
        {
            EnsureNodeStateCapacity(nodeId + 1);
        }

        if (_nodeStates[nodeId] == NodeStateBacklog)
        {
            return;
        }

        if (_nodeStates[nodeId] == NodeStateNone)
        {
            _trackedNodeStateSlots.Add(nodeId);
        }

        _nodeStates[nodeId] = NodeStateBacklog;
        _backlogCount++;

        var remainingDependencies = node.Dependencies.Length + node.OptionalDependencies.Length;
        EnsureDependencyCapacity(nodeId + 1);
        _remainingDependencies[nodeId] = remainingDependencies;
        _trackedDependencySlots.Add(nodeId);

        if (remainingDependencies == 0)
        {
            _ready.Add(node);
        }
    }

    private void MarkMergeFailed(int nodeId, Exception? exception)
    {
        _mergeFailures ??= [];
        _mergeFailures[nodeId] = exception;
    }

    private void ClearPendingMerges()
    {
        while (_pendingMerges.TryDequeue(out var merge))
        {
            merge.DisposeUnmerged();
        }
    }

    private bool RemoveFromBacklog(int nodeId, byte targetState)
    {
        if (!IsInBacklog(nodeId))
        {
            return false;
        }

        _nodeStates[nodeId] = targetState;
        _backlogCount--;
        return true;
    }

    private void ResetNodeStates()
    {
        if (_trackedNodeStateSlots.Count == 0)
        {
            return;
        }

        foreach (var slot in _trackedNodeStateSlots)
        {
            _nodeStates[slot] = NodeStateNone;
        }

        _trackedNodeStateSlots.Clear();
    }

    private void ResetRemainingDependencies()
    {
        if (_trackedDependencySlots.Count == 0)
        {
            return;
        }

        foreach (var slot in _trackedDependencySlots)
        {
            _remainingDependencies[slot] = -1;
        }

        _trackedDependencySlots.Clear();
    }

    private void EnsureNodeStateCapacity(int minCapacity)
    {
        if (_nodeStates.Length >= minCapacity)
        {
            return;
        }

        var newCapacity = _nodeStates.Length == 0 ? 8 : _nodeStates.Length;

        while (newCapacity < minCapacity)
        {
            newCapacity *= 2;
        }

        var nodeStates = ArrayPool<byte>.Shared.Rent(newCapacity);
        nodeStates.AsSpan().Clear();

        if (_nodeStates.Length > 0)
        {
            Array.Copy(_nodeStates, nodeStates, _nodeStates.Length);
            ArrayPool<byte>.Shared.Return(_nodeStates);
        }

        _nodeStates = nodeStates;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkNodeAsSkipped(int nodeId)
        => SetBit(ref _failedOrSkippedBitset, nodeId);

    // Sets the bit of the node and returns whether it was not set before.
    private static bool SetBit(ref ulong[] bitset, int nodeId)
    {
        var index = nodeId >> 6;

        if (index >= bitset.Length)
        {
            EnsureBitsetCapacity(ref bitset, index + 1);
        }

        var mask = 1UL << (nodeId & 63);
        var wasSet = (bitset[index] & mask) != 0;
        bitset[index] |= mask;
        return !wasSet;
    }

    private static void EnsureBitsetCapacity(ref ulong[] bitset, int minWordCount)
    {
        var newCapacity = bitset.Length == 0 ? 2 : bitset.Length;

        while (newCapacity < minWordCount)
        {
            newCapacity *= 2;
        }

        var newBitset = ArrayPool<ulong>.Shared.Rent(newCapacity);
        newBitset.AsSpan().Clear();

        if (bitset.Length > 0)
        {
            bitset.AsSpan().CopyTo(newBitset);
            ArrayPool<ulong>.Shared.Return(bitset);
        }

        bitset = newBitset;
    }

    private static void ReturnBitset(ref ulong[] bitset)
    {
        if (bitset.Length > 0)
        {
            ArrayPool<ulong>.Shared.Return(bitset);
            bitset = [];
        }
    }

    private void ClearFailedOrSkippedBitset()
    {
        if (_failedOrSkippedBitset.Length > 0)
        {
            _failedOrSkippedBitset.AsSpan().Clear();
        }

        if (_completingSkipBitset.Length > 0)
        {
            _completingSkipBitset.AsSpan().Clear();
        }

        if (_nonCompletingSkipBitset.Length > 0)
        {
            _nonCompletingSkipBitset.AsSpan().Clear();
        }

        _hasCompletingSkip = false;
    }
}

/// <summary>
/// The reason a node is skipped without running.
/// </summary>
internal enum SkipKind
{
    /// <summary>
    /// The node was skipped by its own condition, or it was not chosen to run. Its dependent fields are not completed.
    /// </summary>
    NonCompleting,

    /// <summary>
    /// A node that ran failed or skipped itself. The dependent fields of the nodes it skips are completed.
    /// </summary>
    Completing,

    /// <summary>
    /// The node failed because the execution was cancelled. Nothing is recorded.
    /// </summary>
    Cancelled
}
