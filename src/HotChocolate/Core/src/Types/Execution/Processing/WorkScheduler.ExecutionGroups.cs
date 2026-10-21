using HotChocolate.Execution.Processing.Tasks;

namespace HotChocolate.Execution.Processing;

internal sealed partial class WorkScheduler
{
    private ExecutionGroupScope? _groupScopes;
    private volatile bool _hasExecutionGroups;

    /// <summary>
    /// Gets a value indicating whether an execution group scope was opened during this execution.
    /// </summary>
    public bool HasExecutionGroups => _hasExecutionGroups;

    /// <summary>
    /// Opens an execution group scope that stays valid until the scheduler is cleared, or returns
    /// <c>null</c> if the group has nothing to sequence.
    /// </summary>
    public ExecutionGroupScope? OpenExecutionGroupScope(ExecutionGroupPlan plan, ConditionFlags includeFlags)
    {
        AssertNotPooled();

        var scope = ExecutionGroupScope.Open(plan, includeFlags);

        if (scope is null)
        {
            return null;
        }

        _hasExecutionGroups = true;

        ExecutionGroupScope? head;

        do
        {
            head = Volatile.Read(ref _groupScopes);
            scope.NextScope = head;
        }
        while (Interlocked.CompareExchange(ref _groupScopes, scope, head) != head);

        return scope;
    }

    /// <summary>
    /// Counts a task that is registered later with its branch and path, so that neither completes before it.
    /// </summary>
    public void Reserve(ResolverTask task)
    {
        AssertNotPooled();

        lock (_sync)
        {
            RegisterBranchTaskUnsafe(task.BranchId);
            IncrementPathCountUnsafe(task.Selection.FieldSelectionPath);
        }
    }

    /// <summary>
    /// Registers a task that was counted with <see cref="Reserve"/>.
    /// </summary>
    public void RegisterReserved(ResolverTask task)
    {
        AssertNotPooled();

        var work = task.IsSerial ? _serial : _work;
        task.IsRegistered = true;
        task.Id = Interlocked.Increment(ref _nextId);

        lock (_sync)
        {
            work.Push(task);
        }

        _signal.Set();
    }

    private void ReleaseExecutionGroupScopes()
    {
        var scope = Interlocked.Exchange(ref _groupScopes, null);
        var reuse = !_ct.IsCancellationRequested;

        _hasExecutionGroups = false;

        // after an aborted request tasks can still be running, so their scopes are not handed out again.
        while (scope is not null)
        {
            var next = scope.NextScope;

            if (reuse)
            {
                scope.Return();
            }

            scope = next;
        }
    }
}
