using System.Runtime.ExceptionServices;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Collects the cleanup actions of an incremental operation and runs them once the response stream ends.
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
    private readonly CancellationTokenRegistration _registration;
    private List<Func<ValueTask>>? _actions;
    private bool _drained;

    /// <summary>
    /// Initializes a collector that cannot cancel the work of the operation.
    /// </summary>
    public RequestCleanup()
    {
    }

    /// <summary>
    /// Initializes a collector that cancels <paramref name="cancellation"/> when <paramref name="requestAborted"/>
    /// is canceled.
    /// </summary>
    public RequestCleanup(CancellationTokenSource cancellation, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(cancellation);

        _cancellation = cancellation;

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
    /// Stops listening for the cancellation of the request. The collector no longer cancels
    /// its cancellation source afterwards.
    /// </summary>
    public void DetachCancellation()
        => _registration.Dispose();

    /// <summary>
    /// Cancels the work of the operation.
    /// </summary>
    public void Cancel()
        => _cancellation?.Cancel();

    /// <summary>
    /// Runs all registered cleanup actions and marks the collector as executed.
    /// </summary>
    public async ValueTask ExecuteAsync()
    {
        List<Func<ValueTask>>? actions;

        lock (_sync)
        {
            actions = _actions;
            _actions = null;
            _drained = true;
        }

        _registration.Dispose();

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
