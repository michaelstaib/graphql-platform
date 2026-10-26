using System.Runtime.ExceptionServices;
using HotChocolate.Execution.Instrumentation;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Collects the cleanup actions of an incremental operation and runs them once every holder has released it.
/// An action registered after the collector was executed runs immediately.
/// </summary>
internal sealed class RequestCleanup : IRequestCleanup, IAsyncDisposable
{
    private static readonly Node s_drained = new(null!);

    private readonly CancellationTokenSource? _cancellation;
    private readonly CancellationTokenRegistration _registration;
    private readonly RequestContext? _requestContext;
    private readonly ICoreExecutionDiagnosticEvents? _diagnosticEvents;
    private Task? _execution;
    private Node? _head;
    private IDisposable? _last;
    private int _holds = 1;
    private int _executionHeld;

    /// <summary>
    /// Initializes a collector without cancellation and without failure reporting.
    /// </summary>
    public RequestCleanup()
    {
    }

    /// <summary>
    /// Initializes a collector that cancels <paramref name="cancellation"/> when <paramref name="requestAborted"/>
    /// is canceled. Cleanup failures that no response stream can observe are reported
    /// to <paramref name="diagnosticEvents"/> with <paramref name="requestContext"/>.
    /// </summary>
    public RequestCleanup(
        CancellationTokenSource cancellation,
        CancellationToken requestAborted,
        RequestContext requestContext,
        ICoreExecutionDiagnosticEvents diagnosticEvents)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(diagnosticEvents);

        _cancellation = cancellation;
        _requestContext = requestContext;
        _diagnosticEvents = diagnosticEvents;

        if (requestAborted.CanBeCanceled)
        {
            _registration = requestAborted.UnsafeRegister(
                static (state, _) => ((CancellationTokenSource)state!).Cancel(),
                cancellation);
        }
    }

    /// <summary>
    /// Registers a cleanup action that runs when the collector is executed,
    /// or immediately when the collector was already executed.
    /// </summary>
    /// <param name="action">
    /// The cleanup action.
    /// </param>
    public void Register(Func<ValueTask> action)
        => Add(action);

    /// <summary>
    /// Registers a resource that is disposed when the collector is executed,
    /// or immediately when the collector was already executed.
    /// </summary>
    /// <param name="disposable">
    /// The resource to dispose.
    /// </param>
    public void Register(IDisposable disposable)
        => Add(disposable);

    /// <summary>
    /// Registers the resource that is disposed after all other cleanup actions,
    /// or immediately when the collector was already executed.
    /// </summary>
    /// <param name="disposable">
    /// The resource to dispose.
    /// </param>
    public void RegisterLast(IDisposable disposable)
    {
        if (Interlocked.Exchange(ref _last, disposable) is { } previous)
        {
            Add(previous);
        }

        if (ReferenceEquals(Volatile.Read(ref _head), s_drained)
            && Interlocked.Exchange(ref _last, null) is { } late)
        {
            _ = Task.Run(late.Dispose);
        }
    }

    /// <summary>
    /// Adds a holder that keeps the registered actions from running until it releases the collector.
    /// </summary>
    public void Hold()
        => Interlocked.Increment(ref _holds);

    /// <summary>
    /// Adds the hold of <paramref name="execution"/>, which is released when the response stream
    /// is disposed after the execution completed, or when the execution completes afterwards.
    /// </summary>
    public void AttachExecution(Task execution)
    {
        _execution = execution;
        _executionHeld = 1;
        Hold();
    }

    /// <summary>
    /// Stops listening for the cancellation of the request. The collector no longer cancels
    /// its cancellation source afterwards.
    /// </summary>
    public void DetachCancellation()
        => _registration.Dispose();

    /// <summary>
    /// Cancels the work of the operation unless its execution has completed.
    /// </summary>
    public void Cancel()
    {
        if (_cancellation is null || _execution is { IsCompleted: true } || !TryHold())
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        finally
        {
            _ = ReleaseAsync(isBackground: true).AsTask();
        }
    }

    /// <summary>
    /// Releases a hold of background work that no response stream observes.
    /// A cleanup failure is reported to the diagnostic events instead of being thrown.
    /// </summary>
    public ValueTask ReleaseAsync()
        => ReleaseAsync(isBackground: true);

    /// <summary>
    /// Cancels the work of the operation unless its execution has completed, and releases the hold of the caller.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        try
        {
            Cancel();
        }
        catch
        {
            ReleaseExecutionHold();
            _ = ReleaseAsync(isBackground: true).AsTask();
            throw;
        }

        ReleaseExecutionHold();
        return ExecuteAsync();
    }

    /// <summary>
    /// Releases the hold of the caller. The last holder runs all registered cleanup actions
    /// and marks the collector as executed.
    /// </summary>
    public ValueTask ExecuteAsync()
        => ReleaseAsync(isBackground: false);

    private void ReleaseExecutionHold()
    {
        if (Interlocked.Exchange(ref _executionHeld, 0) is 0)
        {
            return;
        }

        var execution = _execution!;

        if (execution.IsCompleted)
        {
            if (execution.IsFaulted)
            {
                _ = execution.Exception;
            }

            // the caller still holds the collector, so this cannot be the last release.
            Interlocked.Decrement(ref _holds);
            return;
        }

        _ = ReleaseWhenSettledAsync(execution);
    }

    private async Task ReleaseWhenSettledAsync(Task execution)
    {
        try
        {
            await execution.ConfigureAwait(false);
        }
        catch
        {
            // an aborted execution has no consumer left that could observe its failure.
        }

        await ReleaseAsync(isBackground: true).ConfigureAwait(false);
    }

    private bool TryHold()
    {
        var holds = Volatile.Read(ref _holds);

        while (holds > 0)
        {
            var seen = Interlocked.CompareExchange(ref _holds, holds + 1, holds);

            if (seen == holds)
            {
                return true;
            }

            holds = seen;
        }

        return false;
    }

    private ValueTask ReleaseAsync(bool isBackground)
        => Interlocked.Decrement(ref _holds) > 0
            ? default
            : DrainAsync(isBackground);

    private async ValueTask DrainAsync(bool isBackground)
    {
        _registration.Dispose();

        var node = Reverse(Interlocked.Exchange(ref _head, s_drained));
        var last = Interlocked.Exchange(ref _last, null);
        var report = isBackground && _diagnosticEvents is not null;
        ExceptionDispatchInfo? exception = null;

        while (true)
        {
            object action;

            if (node is not null)
            {
                action = node.Action;
                node = node.Next;
            }
            else if (last is not null)
            {
                action = last;
                last = null;
            }
            else
            {
                break;
            }

            try
            {
                await RunAsync(action).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (report)
                {
                    _diagnosticEvents!.RequestError(_requestContext!, ex);
                }
                else
                {
                    exception ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
        }

        exception?.Throw();
    }

    private void Add(object action)
    {
        var node = new Node(action);
        var head = Volatile.Read(ref _head);

        while (!ReferenceEquals(head, s_drained))
        {
            node.Next = head;
            var seen = Interlocked.CompareExchange(ref _head, node, head);

            if (ReferenceEquals(seen, head))
            {
                return;
            }

            head = seen;
        }

        _ = Task.Run(() => RunAsync(action).AsTask());
    }

    private static Node? Reverse(Node? head)
    {
        if (ReferenceEquals(head, s_drained))
        {
            return null;
        }

        Node? ordered = null;

        while (head is not null)
        {
            var next = head.Next;
            head.Next = ordered;
            ordered = head;
            head = next;
        }

        return ordered;
    }

    private static ValueTask RunAsync(object action)
    {
        if (action is IDisposable disposable)
        {
            disposable.Dispose();
            return ValueTask.CompletedTask;
        }

        return ((Func<ValueTask>)action).Invoke();
    }

    private sealed class Node(object action)
    {
        public object Action { get; } = action;

        public Node? Next { get; set; }
    }
}
