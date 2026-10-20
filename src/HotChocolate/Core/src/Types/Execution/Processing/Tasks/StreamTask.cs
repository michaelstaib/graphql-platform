using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Text.Json;
using static HotChocolate.Execution.Processing.ValueCompletion;

namespace HotChocolate.Execution.Processing.Tasks;

/// <summary>
/// Streams the remaining items of a list field after its initial items were completed
/// and delivers them in batches through the defer coordinator.
/// </summary>
internal sealed class StreamTask : ExecutionTask
{
    private const int MaxItemsPerPayload = 100;

    private const int Idle = 0;
    private const int Moving = 1;
    private const int DisposeRequested = 2;
    private const int Disposed = 3;

    private readonly OperationContext _parentContext;
    private readonly Selection _selection;
    private readonly object? _parent;
    private readonly IImmutableDictionary<string, object?> _scopedContext;
    private readonly DeferUsage? _deferUsage;
    private readonly Path _path;
    private readonly string? _label;
    private readonly IAsyncEnumerator<object?> _enumerator;
    private readonly CancellationTokenSource _cancellation;
    private readonly MiddlewareContext _resolverContext = new();
    private int _nextItemIndex;
    private int _streamBranchId;
    private int _enumeratorState;

    public StreamTask(
        OperationContext parentContext,
        Selection selection,
        object? parent,
        IImmutableDictionary<string, object?> scopedContext,
        DeferUsage? deferUsage,
        Path path,
        string? label,
        int firstStreamedIndex,
        IAsyncEnumerator<object?> enumerator,
        CancellationTokenSource cancellation)
    {
        _parentContext = parentContext;
        _selection = selection;
        _parent = parent;
        _scopedContext = scopedContext;
        _deferUsage = deferUsage;
        _path = path;
        _label = label;
        _nextItemIndex = firstStreamedIndex;
        _enumerator = enumerator;
        _cancellation = cancellation;
    }

    // the stream task runs in the system branch as it's just an orchestration task.
    public override int BranchId => BranchTracker.SystemBranchId;

    public override bool IsDeferred => true;

    protected override IExecutionTaskContext Context => _parentContext;

    /// <summary>
    /// Announces the stream with the payload of the specified branch.
    /// </summary>
    public void Announce(int parentBranchId)
        => _streamBranchId = _parentContext.DeferExecutionCoordinator.BranchStream(parentBranchId, _path, _label);

    /// <summary>
    /// Stops the stream and disposes the enumerator exactly once.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Cancel();

        while (true)
        {
            var state = Volatile.Read(ref _enumeratorState);

            if (state is DisposeRequested or Disposed)
            {
                return ValueTask.CompletedTask;
            }

            // an enumerator that is moving is disposed by the stream once the move has ended.
            var next = state is Idle ? Disposed : DisposeRequested;

            if (Interlocked.CompareExchange(ref _enumeratorState, next, state) == state)
            {
                return next is Disposed ? DisposeEnumeratorAsync() : ValueTask.CompletedTask;
            }
        }
    }

    protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StreamAsync().ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask StreamAsync()
    {
        var coordinator = _parentContext.DeferExecutionCoordinator;
        var token = _cancellation.Token;
        var items = new List<object?>();
        var move = default(ValueTask<bool>);
        var isMoving = false;

        try
        {
            if (!TryBeginMove())
            {
                return;
            }

            move = ReadNextAsync();
            isMoving = true;

            while (true)
            {
                isMoving = false;

                var hasItem = await move.ConfigureAwait(false);

                if (!hasItem)
                {
                    if (!token.IsCancellationRequested)
                    {
                        coordinator.CompleteStream(_streamBranchId, errors: null);
                    }

                    return;
                }

                items.Clear();
                items.Add(_enumerator.Current);

                // everything the enumerator has readily available is delivered with the same payload.
                var hasEnded = false;

                while (items.Count < MaxItemsPerPayload)
                {
                    if (!TryBeginMove())
                    {
                        return;
                    }

                    move = ReadNextAsync();
                    isMoving = true;

                    if (!move.IsCompletedSuccessfully)
                    {
                        break;
                    }

                    hasItem = move.Result;
                    isMoving = false;

                    if (!hasItem)
                    {
                        hasEnded = true;
                        break;
                    }

                    items.Add(_enumerator.Current);
                }

                if (token.IsCancellationRequested
                    || !await DeliverAsync(items, completesStream: hasEnded).ConfigureAwait(false)
                    || hasEnded)
                {
                    return;
                }

                if (!isMoving)
                {
                    if (!TryBeginMove())
                    {
                        return;
                    }

                    move = ReadNextAsync();
                    isMoving = true;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // the stream was stopped.
        }
        catch (Exception ex)
        {
            coordinator.CompleteStream(_streamBranchId, CreateErrors(ex));
        }
        finally
        {
            if (isMoving)
            {
                Cancel();

                try
                {
                    await move.ConfigureAwait(false);
                }
                catch
                {
                    // the stream already ended, we only wait for the pending move to settle.
                }
            }
        }
    }

    private async ValueTask<bool> DeliverAsync(List<object?> items, bool completesStream)
    {
        var coordinator = _parentContext.DeferExecutionCoordinator;
        var itemsBranchId = coordinator.BranchStreamItems(_streamBranchId);
        var contextFactory = _parentContext.Services.GetRequiredService<IFactory<OperationContextOwner>>();
        using var contextOwner = contextFactory.Create();
        var context = contextOwner.OperationContext;
        var tasks = new List<IExecutionTask>();

        context.InitializeStreamContext(_parentContext, _selection, _path.Parent, _nextItemIndex, itemsBranchId);
        _nextItemIndex += items.Count;

        try
        {
            var listResult = CompleteItems(context, items, tasks, itemsBranchId);

            if (tasks.Count > 0)
            {
                context.Scheduler.Register(CollectionsMarshal.AsSpan(tasks));
            }

            await context.Scheduler.WaitForCompletionAsync(itemsBranchId).ConfigureAwait(false);

            var result = context.BuildResult();

            if (listResult.IsNullOrInvalidated)
            {
                coordinator.EnqueueStreamItems(result, itemsBranchId, items: null, completesStream: completesStream);
                return false;
            }

            var streamedItems = new List<object?>(listResult.GetArrayLength());

            foreach (var item in listResult.EnumerateArray())
            {
                streamedItems.Add(new StreamedItem(item));
            }

            coordinator.EnqueueStreamItems(result, itemsBranchId, streamedItems, completesStream: completesStream);
            return true;
        }
        finally
        {
            // the cleanup registered by the item resolvers is handed to the response stream, also when
            // the batch faults or is cancelled, before the context is returned.
            foreach (var cleanupTask in context.Result.TakeCleanupTasks())
            {
                _parentContext.RegisterRequestCleanup(cleanupTask);
            }
        }
    }

    private ResultElement CompleteItems(
        OperationContext context,
        List<object?> items,
        List<IExecutionTask> tasks,
        int itemsBranchId)
    {
        var listResult = default(ResultElement);

        foreach (var property in context.Result.Data.Data.EnumerateObject())
        {
            listResult = property.Value;
        }

        _resolverContext.Initialize(_parent, _selection, listResult, context, _deferUsage, _scopedContext);
        _resolverContext.BranchId = itemsBranchId;

        try
        {
            var completionContext = new ValueCompletionContext(context, _resolverContext, tasks, itemsBranchId);
            Complete(completionContext, _selection, listResult, items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.ReportError(ex, _resolverContext, listResult.Path);
            listResult.SetNullValue();
        }
        finally
        {
            _resolverContext.Clean();
        }

        if (listResult is { IsNullable: false, IsNullOrInvalidated: true })
        {
            if (context.PropagateNullValues)
            {
                PropagateNullValues(listResult);
            }
            else
            {
                listResult.SetNullValue();
            }

            context.Result.AddNonNullViolation(listResult.Path);
            tasks.Clear();
        }

        return listResult;
    }

    private ImmutableList<IError> CreateErrors(Exception exception)
    {
        var errors = ImmutableList.CreateBuilder<IError>();

        if (exception is GraphQLException graphQLException)
        {
            foreach (var error in graphQLException.Errors)
            {
                Add(error.Path is null ? error.WithPath(_path) : error);
            }
        }
        else
        {
            Add(ErrorHelper.StreamEnumerationFailed(exception, _path));
        }

        return errors.ToImmutable();

        void Add(IError error)
        {
            var handled = _parentContext.ErrorHandler.Handle(error);

            if (handled is AggregateError aggregateError)
            {
                errors.AddRange(aggregateError.Errors);
            }
            else
            {
                errors.Add(handled);
            }
        }
    }

    private void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the stream already ended and released its resources.
        }
    }

    private bool TryBeginMove()
        => Interlocked.CompareExchange(ref _enumeratorState, Moving, Idle) == Idle;

    private async ValueTask<bool> ReadNextAsync()
    {
        bool hasItem;

        try
        {
            hasItem = await _enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch
        {
            await EndMoveAsync().ConfigureAwait(false);
            throw;
        }

        // an enumerator that was disposed during the move has no item to offer.
        return await EndMoveAsync().ConfigureAwait(false) && hasItem;
    }

    private async ValueTask<bool> EndMoveAsync()
    {
        if (Interlocked.CompareExchange(ref _enumeratorState, Idle, Moving) == Moving)
        {
            return true;
        }

        Volatile.Write(ref _enumeratorState, Disposed);
        await DisposeEnumeratorAsync().ConfigureAwait(false);
        return false;
    }

    private async ValueTask DisposeEnumeratorAsync()
    {
        try
        {
            await _enumerator.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private sealed class StreamedItem(ResultElement element) : IResultDataJsonFormatter
    {
        public void WriteTo(JsonWriter writer, JsonSerializerOptions? options = null)
            => element.WriteTo(writer);
    }
}
