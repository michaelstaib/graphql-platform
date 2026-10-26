using System.Runtime.ExceptionServices;
using HotChocolate.Execution.Instrumentation;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Collects the cleanup actions of an incremental operation and runs them once every holder has released it.
/// An action registered after the collector was executed runs immediately.
/// </summary>
internal sealed class RequestCleanup : IRequestCleanup, IAsyncDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly CancellationTokenSource? _cancellation;
    private readonly RequestContext? _requestContext;
    private readonly ICoreExecutionDiagnosticEvents? _diagnosticEvents;
    private List<object>? _actions;
    private IDisposable? _last;
    private int _holds = 1;
    private bool _drained;
    private bool _settled;

    /// <summary>
    /// Initializes a collector without cancellation and without failure reporting.
    /// </summary>
    public RequestCleanup()
    {
    }

    /// <summary>
    /// Initializes a collector that owns <paramref name="cancellation"/>, which is disposed after
    /// the cleanup actions ran. Cleanup failures that no response stream can observe are reported
    /// to <paramref name="diagnosticEvents"/> with <paramref name="requestContext"/>.
    /// </summary>
    public RequestCleanup(
        CancellationTokenSource cancellation,
        RequestContext requestContext,
        ICoreExecutionDiagnosticEvents diagnosticEvents)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(diagnosticEvents);

        _cancellation = cancellation;
        _requestContext = requestContext;
        _diagnosticEvents = diagnosticEvents;
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
        lock (_sync)
        {
            if (!_drained)
            {
                _last = disposable;
                return;
            }
        }

        _ = Task.Run(disposable.Dispose);
    }

    /// <summary>
    /// Adds a holder that keeps the registered actions from running until it releases the collector.
    /// </summary>
    public void Hold()
    {
        lock (_sync)
        {
            _holds++;
        }
    }

    /// <summary>
    /// Cancels the work of the operation unless it has settled.
    /// </summary>
    public void Cancel()
    {
        CancellationTokenSource? source;

        lock (_sync)
        {
            source = _settled ? null : _cancellation;
        }

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the operation already released its resources.
        }
    }

    /// <summary>
    /// Releases the hold of the work that executes the operation, which has run to its end.
    /// A cleanup failure is reported to the diagnostic events instead of being thrown.
    /// </summary>
    public ValueTask SettleAsync()
    {
        lock (_sync)
        {
            _settled = true;
        }

        return ReleaseAsync();
    }

    /// <summary>
    /// Releases a hold of background work that no response stream observes.
    /// A cleanup failure is reported to the diagnostic events instead of being thrown.
    /// </summary>
    public ValueTask ReleaseAsync()
        => ReleaseAsync(isBackground: true);

    /// <summary>
    /// Cancels the work of the operation unless it has settled, and releases the hold of the caller.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            Cancel();
        }
        finally
        {
            await ExecuteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the hold of the caller. The last holder runs all registered cleanup actions
    /// and marks the collector as executed.
    /// </summary>
    public ValueTask ExecuteAsync()
        => ReleaseAsync(isBackground: false);

    private async ValueTask ReleaseAsync(bool isBackground)
    {
        List<object>? actions;

        lock (_sync)
        {
            if (--_holds > 0)
            {
                return;
            }

            actions = _actions;
            _actions = null;
            _drained = true;

            if (_last is { } last)
            {
                (actions ??= []).Add(last);
                _last = null;
            }
        }

        var report = isBackground && _diagnosticEvents is not null;
        ExceptionDispatchInfo? exception = null;

        try
        {
            if (actions is not null)
            {
                foreach (var action in actions)
                {
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
            }
        }
        finally
        {
            _cancellation?.Dispose();
        }

        exception?.Throw();
    }

    private void Add(object action)
    {
        lock (_sync)
        {
            if (!_drained)
            {
                (_actions ??= []).Add(action);
                return;
            }
        }

        _ = Task.Run(() => RunAsync(action).AsTask());
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
}
