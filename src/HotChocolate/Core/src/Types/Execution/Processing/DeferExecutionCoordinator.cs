using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HotChocolate.Fetching;

namespace HotChocolate.Execution.Processing;

internal sealed partial class DeferExecutionCoordinator
{
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly Dictionary<DeferredBranchKey, int> _branchIdLookup = [];
    private readonly Dictionary<int, DeferredBranch> _branchLookup = [];
    private HashSet<int>? _mainBranchChildren;
    private readonly Dictionary<int, OperationResult> _completed = [];
    private readonly HashSet<int> _delivered = [];
    private readonly List<OperationResult> _results = [];
    private readonly AsyncAutoResetEvent _signal = new();
    private BranchTracker _branchTracker = null!;
    private int _mainBranchId;
    private ImmutableList<PendingResult>.Builder? _pendingBuilder;
    private ImmutableList<IIncrementalResult>.Builder? _incrementalBuilder;
    private ImmutableList<CompletedResult>.Builder? _completedBuilder;
    private Queue<int>? _processQueue;
    private volatile bool _hasBranches;
    private volatile bool _isComplete;
    private int _pendingBranches;

#pragma warning disable IDE0052 // Remove unread private members
    private static int s_nextId;
    private readonly int _id;
#pragma warning restore IDE0052 // Remove unread private members

    public DeferExecutionCoordinator()
    {
        _id = Interlocked.Increment(ref s_nextId);
    }

    /// <summary>
    /// Gets whether any deferred execution branches have been registered.
    /// </summary>
    public bool HasBranches => _hasBranches;

    /// <summary>
    /// Registers a new deferred execution branch for the specified <paramref name="deferUsage"/>
    /// and <paramref name="path"/>, returning a unique branch identifier.
    /// If the branch was already registered, the existing identifier is returned.
    /// </summary>
    public int Branch(int currentBranchId, Path path, DeferUsage deferUsage)
    {
        AssertInitialized();

        var key = new DeferredBranchKey(path, deferUsage, currentBranchId);

        lock (_sync)
        {
            if (!_branchIdLookup.TryGetValue(key, out var newBranchId))
            {
                newBranchId = _branchTracker.CreateNewBranchId();
                GetChildrenUnsafe(currentBranchId).Add(newBranchId);
                _branchLookup.Add(
                    newBranchId,
                    new DeferredBranch(path, deferUsage.Label, deferUsage, currentBranchId, BranchKind.Defer));
                _branchIdLookup.Add(key, newBranchId);
                _hasBranches = true;
                _pendingBranches++;
            }

            return newBranchId;
        }
    }

    /// <summary>
    /// Registers a streamed list at <paramref name="path"/> that is announced with the payload of
    /// <paramref name="currentBranchId"/>, returning a unique branch identifier.
    /// </summary>
    public int BranchStream(int currentBranchId, Path path, string? label)
    {
        AssertInitialized();

        var key = new DeferredBranchKey(path, null, currentBranchId);

        lock (_sync)
        {
            if (!_branchIdLookup.TryGetValue(key, out var streamBranchId))
            {
                streamBranchId = _branchTracker.CreateNewBranchId();
                GetChildrenUnsafe(currentBranchId).Add(streamBranchId);
                _branchLookup.Add(
                    streamBranchId,
                    new DeferredBranch(path, label, null, currentBranchId, BranchKind.Stream)
                    {
                        Stream = new StreamState()
                    });
                _branchIdLookup.Add(key, streamBranchId);
                _hasBranches = true;
                _pendingBranches++;
            }

            return streamBranchId;
        }
    }

    /// <summary>
    /// Registers the next batch of items of the streamed list <paramref name="streamBranchId"/>,
    /// returning the branch identifier under which the batch is executed.
    /// </summary>
    public int BranchStreamItems(int streamBranchId)
    {
        AssertInitialized();

        lock (_sync)
        {
            var stream = _branchLookup[streamBranchId];
            var itemsBranchId = _branchTracker.CreateNewBranchId();
            _branchLookup.Add(
                itemsBranchId,
                new DeferredBranch(stream.Path, null, null, streamBranchId, BranchKind.StreamItems));
            (stream.Stream!.Items ??= []).Add(itemsBranchId);
            return itemsBranchId;
        }
    }

    /// <summary>
    /// Enqueues the result of a batch of streamed items. The <paramref name="items"/> are the list items
    /// to deliver, or <c>null</c> if the errors of the batch bubbled above the streamed list.
    /// If <paramref name="completesStream"/> is <c>true</c>, the batch is the last one of the stream.
    /// </summary>
    public void EnqueueStreamItems(
        OperationResult result,
        int itemsBranchId,
        IReadOnlyList<object?>? items,
        bool completesStream)
    {
        AssertInitialized();

        lock (_sync)
        {
            ref var itemsBranch = ref CollectionsMarshal.GetValueRefOrNullRef(_branchLookup, itemsBranchId);
            itemsBranch.StreamItems = items;

            if (completesStream)
            {
                _branchLookup[itemsBranch.ParentBranchId].Stream!.IsCompleted = true;
            }
        }

        EnqueueResult(result, itemsBranchId);
    }

    /// <summary>
    /// Completes the streamed list <paramref name="streamBranchId"/> after its last batch of items was enqueued.
    /// The <paramref name="errors"/> are the errors that ended the stream, or <c>null</c> if it ended regularly.
    /// </summary>
    public void CompleteStream(int streamBranchId, IReadOnlyList<IError>? errors)
    {
        AssertInitialized();

        lock (_sync)
        {
            var stream = _branchLookup[streamBranchId].Stream!;

            if (stream.IsCompleted)
            {
                return;
            }

            stream.IsCompleted = true;
            stream.Errors = errors;

            if (!_delivered.Contains(streamBranchId))
            {
                return;
            }

            var completedBuilder = _completedBuilder ??= ImmutableList.CreateBuilder<CompletedResult>();
            completedBuilder.Clear();

            if (TryAddStreamCompletionUnsafe(streamBranchId, completedBuilder))
            {
                var result = new OperationResult(
                    new IncrementalDataFeature
                    {
                        Completed = completedBuilder.ToImmutable()
                    });

                PublishUnsafe(result);
            }
        }
    }

    /// <summary>
    /// Unregisters the child branches of <paramref name="parentBranchId"/> whose tasks were dropped
    /// before they started, so the response no longer waits for them.
    /// </summary>
    public void ReleaseChildBranches(int parentBranchId)
    {
        AssertInitialized();

        lock (_sync)
        {
            ref var parent = ref CollectionsMarshal.GetValueRefOrNullRef(_branchLookup, parentBranchId);

            if (Unsafe.IsNullRef(ref parent) || parent.Children is not { Count: > 0 } children)
            {
                return;
            }

            foreach (var childId in children)
            {
                if (_branchLookup.Remove(childId, out var child))
                {
                    _branchIdLookup.Remove(new DeferredBranchKey(child.Path, child.Group, child.ParentBranchId));
                    _pendingBranches--;
                }
            }

            children.Clear();
        }
    }

    /// <summary>
    /// Enqueues the initial (non-deferred) result for delivery.
    /// Any already-completed child branches are folded in as incremental data.
    /// </summary>
    public void EnqueueResult(OperationResult result)
    {
        AssertInitialized();

        lock (_sync)
        {
            ComposeAndDeliverUnsafe(_mainBranchId, result);
        }
    }

    /// <summary>
    /// Enqueues a deferred result for the specified branch.
    /// If the parent branch has already been delivered, the result is composed
    /// and delivered immediately; otherwise it is stored until the parent is delivered.
    /// </summary>
    public void EnqueueResult(OperationResult result, int branchId)
    {
        AssertInitialized();

        lock (_sync)
        {
            _completed[branchId] = result;

            if (IsParentDeliveredUnsafe(branchId)
                && _completed.Remove(branchId, out var readyResult))
            {
                ComposeAndDeliverUnsafe(branchId, readyResult);
            }
        }
    }

    /// <summary>
    /// Returns an async stream of composed operation results in delivery order.
    /// The stream completes automatically when all branches have been delivered.
    /// </summary>
    public async IAsyncEnumerable<OperationResult> ReadResultsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AssertInitialized();

        List<OperationResult>? snapshot = null;
        await using var registration = cancellationToken.Register(_signal.Set);

        while (!cancellationToken.IsCancellationRequested)
        {
            await _signal;

            cancellationToken.ThrowIfCancellationRequested();

            // Read both the results and the completion flag inside the same lock
            // to avoid a race where a final delivery sets _isComplete between
            // the snapshot read and the completion check.
            bool isComplete;

            lock (_sync)
            {
                snapshot ??= [];
                snapshot.Clear();
                snapshot.AddRange(_results);
                _results.Clear();
                isComplete = _isComplete;
            }

            foreach (var result in snapshot)
            {
                yield return result;
            }

            if (isComplete)
            {
                yield break;
            }
        }
    }

    private void ComposeAndDeliverUnsafe(int branchId, OperationResult result)
    {
        var children = GetChildrenUnsafe(branchId);

        if (children.Count > 0)
        {
            var pendingBuilder = _pendingBuilder ??= ImmutableList.CreateBuilder<PendingResult>();
            var incrementalBuilder = _incrementalBuilder ??= ImmutableList.CreateBuilder<IIncrementalResult>();
            var completedBuilder = _completedBuilder ??= ImmutableList.CreateBuilder<CompletedResult>();
            var processQueue = _processQueue ??= new Queue<int>();

            pendingBuilder.Clear();
            incrementalBuilder.Clear();
            completedBuilder.Clear();
            processQueue.Clear();

            foreach (var childId in children)
            {
                AnnounceChildUnsafe(
                    childId,
                    result,
                    pendingBuilder,
                    incrementalBuilder,
                    completedBuilder,
                    processQueue);
            }

            while (processQueue.TryDequeue(out var parentId))
            {
                foreach (var grandchildId in GetChildrenUnsafe(parentId))
                {
                    AnnounceChildUnsafe(
                        grandchildId,
                        result,
                        pendingBuilder,
                        incrementalBuilder,
                        completedBuilder,
                        processQueue);
                }
            }

            result.Pending = pendingBuilder.ToImmutable();
            result.Incremental = incrementalBuilder.ToImmutable();
            result.Completed = completedBuilder.ToImmutable();
        }

        // For deferred branches (not main branch), transform the result's data into an incremental result.
        // Per spec: only the initial payload has root "data"; subsequent payloads use "incremental" array.
        if (branchId != _mainBranchId)
        {
            var incrementalBuilder = _incrementalBuilder ??= ImmutableList.CreateBuilder<IIncrementalResult>();
            var completedBuilder = _completedBuilder ??= ImmutableList.CreateBuilder<CompletedResult>();

            if (children.Count == 0)
            {
                incrementalBuilder.Clear();
                completedBuilder.Clear();
            }

            var branch = _branchLookup[branchId];

            if (branch.Kind is BranchKind.StreamItems)
            {
                AddStreamItemsUnsafe(branch, result, incrementalBuilder, completedBuilder);
                TryAddStreamCompletionUnsafe(branch.ParentBranchId, completedBuilder);
            }
            else
            {
                AddCompletedBranch(branchId, result, incrementalBuilder, completedBuilder);
            }

            result.Incremental = incrementalBuilder.ToImmutable();
            result.Completed = completedBuilder.ToImmutable();
            result.Data = null;
            result.Errors = [];
        }

        _delivered.Add(branchId);

        if (branchId != _mainBranchId && _branchLookup[branchId].Kind is not BranchKind.StreamItems)
        {
            _pendingBranches--;
        }

        PublishUnsafe(result);
    }

    private void PublishUnsafe(OperationResult result)
    {
        var isComplete = _delivered.Contains(_mainBranchId) && _pendingBranches == 0;
        result.HasNext = !isComplete;

        _results.Add(result);
        _isComplete = isComplete;
        _signal.Set();
    }

    /// <summary>
    /// Announces a child branch with the payload that is being composed and folds in everything
    /// the child has already completed.
    /// </summary>
    private void AnnounceChildUnsafe(
        int childId,
        OperationResult result,
        ImmutableList<PendingResult>.Builder pendingBuilder,
        ImmutableList<IIncrementalResult>.Builder incrementalBuilder,
        ImmutableList<CompletedResult>.Builder completedBuilder,
        Queue<int> processQueue)
    {
        var child = _branchLookup[childId];

        pendingBuilder.Add(new PendingResult(childId, child.Path, child.Label));

        if (child.Kind is BranchKind.Stream)
        {
            _delivered.Add(childId);

            var stream = child.Stream!;

            while (stream.Items is { } items
                && stream.DeliveredItems < items.Count
                && _completed.Remove(items[stream.DeliveredItems], out var itemsResult))
            {
                var itemsBranchId = items[stream.DeliveredItems];

                result.RegisterForCleanup(itemsResult);
                AddStreamItemsUnsafe(_branchLookup[itemsBranchId], itemsResult, incrementalBuilder, completedBuilder);
                _delivered.Add(itemsBranchId);
                processQueue.Enqueue(itemsBranchId);
            }

            TryAddStreamCompletionUnsafe(childId, completedBuilder);
        }
        else if (_completed.Remove(childId, out var childResult))
        {
            result.RegisterForCleanup(childResult);
            AddCompletedBranch(childId, childResult, incrementalBuilder, completedBuilder);
            _delivered.Add(childId);
            _pendingBranches--;
            processQueue.Enqueue(childId);
        }
    }

    /// <summary>
    /// Determines whether the parent of the specified branch has already
    /// delivered its result to the response stream.
    /// </summary>
    private bool IsParentDeliveredUnsafe(int branchId)
        => _branchLookup.TryGetValue(branchId, out var branch)
            && _delivered.Contains(branch.ParentBranchId);

    private static void AddCompletedBranch(
        int branchId,
        OperationResult branchResult,
        ImmutableList<IIncrementalResult>.Builder incrementalBuilder,
        ImmutableList<CompletedResult>.Builder completedBuilder)
    {
        if (branchResult.Data.HasValue && !branchResult.Data.Value.IsValueNull)
        {
            // data is valid (possibly with contained errors) — deliver incremental data
            incrementalBuilder.Add(
                new IncrementalObjectResult(
                    branchId,
                    branchResult.Errors,
                    subPath: null,
                    branchResult.Data));
            completedBuilder.Add(new CompletedResult(branchId));
        }
        else
        {
            // errors bubbled above the incremental result's path — no data to deliver
            completedBuilder.Add(new CompletedResult(branchId, branchResult.Errors));
        }
    }

    private void AddStreamItemsUnsafe(
        DeferredBranch itemsBranch,
        OperationResult itemsResult,
        ImmutableList<IIncrementalResult>.Builder incrementalBuilder,
        ImmutableList<CompletedResult>.Builder completedBuilder)
    {
        var streamBranchId = itemsBranch.ParentBranchId;
        var stream = _branchLookup[streamBranchId].Stream!;

        stream.DeliveredItems++;

        if (itemsBranch.StreamItems is { } items)
        {
            incrementalBuilder.Add(new IncrementalListResult(streamBranchId, itemsResult.Errors, items));
        }
        else if (!stream.IsCompletionDelivered)
        {
            // errors bubbled above the streamed list, so the stream ends with these errors.
            completedBuilder.Add(new CompletedResult(streamBranchId, itemsResult.Errors));
            stream.IsCompleted = true;
            stream.IsCompletionDelivered = true;
            _pendingBranches--;
        }
    }

    private bool TryAddStreamCompletionUnsafe(
        int streamBranchId,
        ImmutableList<CompletedResult>.Builder completedBuilder)
    {
        var stream = _branchLookup[streamBranchId].Stream!;

        if (!stream.IsCompleted
            || stream.IsCompletionDelivered
            || stream.DeliveredItems != (stream.Items?.Count ?? 0)
            || !_delivered.Contains(streamBranchId))
        {
            return false;
        }

        stream.IsCompletionDelivered = true;
        completedBuilder.Add(new CompletedResult(streamBranchId, stream.Errors));
        _pendingBranches--;
        return true;
    }

    /// <summary>
    /// Gets the child branches for the specified branch.
    /// For the main branch, uses the dedicated field; for deferred branches,
    /// uses the children set stored in the branch lookup.
    /// </summary>
    private HashSet<int> GetChildrenUnsafe(int branchId)
    {
        if (branchId == _mainBranchId)
        {
            return _mainBranchChildren ??= [];
        }

        ref var branch = ref CollectionsMarshal.GetValueRefOrNullRef(_branchLookup, branchId);

        if (Unsafe.IsNullRef(ref branch))
        {
            return [];
        }

        return branch.Children ??= [];
    }

    private readonly record struct DeferredBranchKey(Path Path, DeferUsage? Group, int ParentBranchId);

    private enum BranchKind
    {
        Defer,
        Stream,
        StreamItems
    }

    private struct DeferredBranch(
        Path path,
        string? label,
        DeferUsage? group,
        int parentBranchId,
        BranchKind kind)
    {
        public Path Path { get; } = path;
        public string? Label { get; } = label;
        public DeferUsage? Group { get; } = group;
        public int ParentBranchId { get; } = parentBranchId;
        public BranchKind Kind { get; } = kind;
        public HashSet<int>? Children { get; set; }
        public StreamState? Stream { get; set; }
        public IReadOnlyList<object?>? StreamItems { get; set; }
    }

    private sealed class StreamState
    {
        public List<int>? Items { get; set; }
        public int DeliveredItems { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCompletionDelivered { get; set; }
        public IReadOnlyList<IError>? Errors { get; set; }
    }
}
