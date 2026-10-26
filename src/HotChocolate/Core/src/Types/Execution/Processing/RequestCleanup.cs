using System.Runtime.ExceptionServices;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Collects the cleanup actions of an incremental operation and runs them once every holder has released it.
/// An action registered after the collector was executed runs immediately.
/// </summary>
internal sealed class RequestCleanup
{
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly CancellationTokenSource? _cancellation;
    private List<Func<ValueTask>>? _actions;
    private int _holds = 1;
    private bool _drained;
    private bool _settled;

    /// <summary>
    /// Initializes a collector without cancellation.
    /// </summary>
    public RequestCleanup()
    {
    }

    /// <summary>
    /// Initializes a collector that owns <paramref name="cancellation"/>, which is disposed after
    /// the cleanup actions ran.
    /// </summary>
    public RequestCleanup(CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        _cancellation = cancellation;
    }

    /// <summary>
    /// Registers a cleanup action that runs when the collector is executed,
    /// or immediately when the collector was already executed.
    /// </summary>
    /// <param name="action">
    /// The cleanup action.
    /// </param>
    public void Register(Func<ValueTask> action)
    {
        lock (_sync)
        {
            if (!_drained)
            {
                (_actions ??= []).Add(action);
                return;
            }
        }

        _ = Task.Run(() => action().AsTask());
    }

    /// <summary>
    /// Adds a holder that keeps the registered actions from running until it calls
    /// <see cref="ExecuteAsync"/> or <see cref="SettleAsync"/>.
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
    /// </summary>
    public ValueTask SettleAsync()
    {
        lock (_sync)
        {
            _settled = true;
        }

        return ExecuteAsync();
    }

    /// <summary>
    /// Cancels the work of the operation unless it has settled, and releases the hold of the caller.
    /// </summary>
    public async ValueTask AbandonAsync()
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
    public async ValueTask ExecuteAsync()
    {
        List<Func<ValueTask>>? actions;

        lock (_sync)
        {
            if (--_holds > 0)
            {
                return;
            }

            actions = _actions;
            _actions = null;
            _drained = true;
        }

        try
        {
            await RunAsync(actions).ConfigureAwait(false);
        }
        finally
        {
            _cancellation?.Dispose();
        }
    }

    private static async ValueTask RunAsync(List<Func<ValueTask>>? actions)
    {
        if (actions is null)
        {
            return;
        }

        ExceptionDispatchInfo? exception = null;

        foreach (var action in actions)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exception ??= ExceptionDispatchInfo.Capture(ex);
            }
        }

        exception?.Throw();
    }
}
