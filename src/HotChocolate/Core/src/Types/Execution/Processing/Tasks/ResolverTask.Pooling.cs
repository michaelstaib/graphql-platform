using System.Collections.Immutable;
using HotChocolate.Text.Json;

namespace HotChocolate.Execution.Processing.Tasks;

internal sealed partial class ResolverTask
{
    /// <summary>
    /// Initializes this task after it is retrieved from its pool.
    /// </summary>
    public void Initialize(
        object? parent,
        Selection selection,
        ResultElement resultValue,
        OperationContext operationContext,
        IImmutableDictionary<string, object?> scopedContextData,
        int executionBranchId,
        DeferUsage? deferUsage)
    {
        _operationContext = operationContext;
        _selection = selection;
        _context.Initialize(parent, selection, resultValue, operationContext, deferUsage, scopedContextData);
        _context.BranchId = executionBranchId;
        IsSerial = selection.Strategy is SelectionExecutionStrategy.Serial;
        BranchId = executionBranchId;
        DeferUsage = deferUsage;
    }

    /// <summary>
    /// Makes this task a participant of the execution group scope if the selection is part of its plan.
    /// </summary>
    internal void JoinExecutionGroup(ExecutionGroupScope? scope)
    {
        if (scope is not null && scope.Plan.TryGetNode(_selection.Id, out var node))
        {
            _groupScope = scope;
            _groupSlot = node.SlotIndex;
        }
    }

    /// <summary>
    /// Registers this task with the work scheduler.
    /// </summary>
    public void Register() => _operationContext.Scheduler.Register(this);

    /// <summary>
    /// Counts this task with the work scheduler without making it available for execution.
    /// </summary>
    public void Reserve() => _operationContext.Scheduler.Reserve(this);

    /// <summary>
    /// Makes this task available for execution after it was counted with <see cref="Reserve"/>.
    /// </summary>
    public void RegisterReserved() => _operationContext.Scheduler.RegisterReserved(this);

    /// <summary>
    /// Resets the resolver task before returning it to the pool.
    /// </summary>
    /// <returns>Always <c>true</c>.</returns>
    internal bool Reset()
    {
        _completionStatus = ExecutionTaskStatus.Completed;
        _operationContext = null!;
        _selection = null!;
        _context.Clean();
        Status = ExecutionTaskStatus.WaitingToRun;
        IsSerial = false;
        BranchId = int.MinValue;
        DeferUsage = null;
        _groupScope = null;
        _groupSlot = -1;
        IsRegistered = false;
#pragma warning disable CS0618
        Next = null;
        Previous = null;
#pragma warning restore CS0618
        State = null;
        _taskBuffer.Clear();
        _args.Clear();
        return true;
    }
}
