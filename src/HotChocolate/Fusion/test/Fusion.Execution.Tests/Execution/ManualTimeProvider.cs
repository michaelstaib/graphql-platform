namespace HotChocolate.Fusion.Execution;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when <see cref="Advance"/> is called.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private TimeSpan _elapsed;
    private int _created;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return DateTimeOffset.UnixEpoch + _elapsed;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        List<TaskCompletionSource> signals = [];

        lock (_sync)
        {
            _timers.Add(timer);
            timer.Change(dueTime, period);
            _created++;

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_created >= _waiters[i].Count)
                {
                    signals.Add(_waiters[i].Signal);
                    _waiters.RemoveAt(i);
                }
            }
        }

        foreach (var signal in signals)
        {
            signal.TrySetResult();
        }

        return timer;
    }

    /// <summary>
    /// Completes once at least <paramref name="count"/> timers have been created on this provider.
    /// </summary>
    public Task WaitForTimersAsync(int count)
    {
        lock (_sync)
        {
            if (_created >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, signal));
            return signal.Task;
        }
    }

    /// <summary>
    /// Moves the clock forward and fires every timer that became due.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        List<ManualTimer> due = [];

        lock (_sync)
        {
            _elapsed += delta;

            foreach (var timer in _timers)
            {
                if (timer.TryTakeDue(_elapsed))
                {
                    due.Add(timer);
                }
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan? _dueAt;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._elapsed + dueTime;
                _period = period;
                return true;
            }
        }

        public bool TryTakeDue(TimeSpan now)
        {
            if (_dueAt is not { } dueAt || dueAt > now)
            {
                return false;
            }

            _dueAt = _period == Timeout.InfiniteTimeSpan ? null : dueAt + _period;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._sync)
            {
                _dueAt = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
