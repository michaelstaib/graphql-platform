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
    private List<Func<ValueTask>>? _actions;
    private bool _drained;

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

        if (actions is null)
        {
            return;
        }

        foreach (var action in actions)
        {
            await action().ConfigureAwait(false);
        }
    }
}
