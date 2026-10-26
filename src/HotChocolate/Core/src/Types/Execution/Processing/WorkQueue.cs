using System.Diagnostics.CodeAnalysis;

namespace HotChocolate.Execution.Processing;

internal sealed class WorkQueue
{
    private readonly Stack<IExecutionTask> _immediateStack = new();
    private readonly Stack<IExecutionTask> _deferredStack = new();
    private int _running;
    private int _parked;

    public bool IsEmpty => _immediateStack.Count == 0 && _deferredStack.Count == 0;

    public bool HasRunningTasks => _running > 0;

    public bool HasBlockingTasks => _running > _parked;

    public void Park() => _parked++;

    public void Unpark() => _parked--;

    public bool Complete()
    {
        var value = Interlocked.Decrement(ref _running);

        if (value < 0)
        {
            throw new InvalidOperationException();
        }

        return value is 0;
    }

    public bool TryTake([MaybeNullWhen(false)] out IExecutionTask executionTask)
    {
        if (_immediateStack.TryPop(out executionTask)
            || _deferredStack.TryPop(out executionTask))
        {
            Interlocked.Increment(ref _running);
            return true;
        }

        return false;
    }

    public void Push(IExecutionTask executionTask)
    {
        ArgumentNullException.ThrowIfNull(executionTask);

        if (executionTask.IsDeferred)
        {
            _deferredStack.Push(executionTask);
        }
        else
        {
            _immediateStack.Push(executionTask);
        }
    }

    public void Clear()
    {
        _immediateStack.Clear();
        _deferredStack.Clear();
        _running = 0;
        _parked = 0;
    }
}
