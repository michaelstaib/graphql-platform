using System.Buffers;
using System.Collections.Immutable;
using HotChocolate.Execution.DependencyInjection;

namespace HotChocolate.Execution.Processing.Tasks;

internal sealed class DeferTask : ExecutionTask
{
    private static readonly ArrayPool<IExecutionTask> s_pool = ArrayPool<IExecutionTask>.Shared;
    private OperationContextOwner _deferContextOwner = null!;
    private object? _parent;
    private IImmutableDictionary<string, object?> _scopedContext = null!;
    private ExecutionGroupScope? _groupScope;
    private int _executionBranchId;
    private DeferUsage _deferUsage = null!;
    private Path _selectionPath = null!;

    // the defer tasks runs in the system branch as it's just an orchestration task.
    public override int BranchId => BranchTracker.SystemBranchId;

    public override bool IsDeferred => true;

    protected override IExecutionTaskContext Context => _deferContextOwner.OperationContext;

    protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        var deferContext = _deferContextOwner.OperationContext;
        var data = deferContext.Result.Data.Data;
        var bufferedTasks = s_pool.Rent(data.GetPropertyCount());
        var i = 0;

        try
        {
            foreach (var field in data.EnumerateObject())
            {
                var selection = field.AssertSelection();
                var task =
                    ResolverTaskFactory.CreateOrRegisterTopLevelTask(
                        deferContext,
                        _parent,
                        selection,
                        field.Value,
                        _scopedContext,
                        _executionBranchId,
                        _deferUsage);

                if (task is null)
                {
                    continue;
                }

                task.JoinExecutionGroup(_groupScope);

                bufferedTasks[i++] = task;
            }

            // we register our deferred tasks for execution ...
            deferContext.Scheduler.Register(bufferedTasks.AsSpan(0, i));
        }
        finally
        {
            if (i > 0)
            {
                bufferedTasks.AsSpan(0, i).Clear();
            }

            s_pool.Return(bufferedTasks);
        }

        deferContext.Scheduler.ParkRunningTask();

        try
        {
            // ... and then wait for the scheduler to complete the deferred tasks. An aborted request
            // still waits for them, so the context is not reset while they use it.
            await deferContext.Scheduler.WaitForSettlementAsync(_executionBranchId);
            cancellationToken.ThrowIfCancellationRequested();

            // once the execution branch has completed we enqueue the completed
            // result with the defer coordinator so it can be delivered.
            var result = deferContext.BuildResult();
            await deferContext.DeferExecutionCoordinator.EnqueueResult(result, _executionBranchId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // a failure after the branch settled completes the branch with an error result.
            var failedResult = new OperationResult(CreateErrors(deferContext, ex));
            await deferContext.DeferExecutionCoordinator.EnqueueResult(failedResult, _executionBranchId);
        }
        finally
        {
            deferContext.Scheduler.UnparkRunningTask();

            // the request cleanup registered by the deferred resolvers is handed to the collector of
            // the response stream, also when the branch faults or is cancelled, before the context is reset.
            var requestCleanup = deferContext.RequestCleanup!;

            foreach (var cleanupTask in deferContext.Result.TakeCleanupTasks())
            {
                requestCleanup.Register(cleanupTask);
            }
        }
    }

    protected override ValueTask OnAfterCompletedAsync(CancellationToken cancellationToken)
    {
        // TODO : we need to give the context back here and not rest once we have a pool.
        Reset();
        return ValueTask.CompletedTask;
    }

    public void Initialize(
        OperationContext parentContext,
        object? parent,
        IImmutableDictionary<string, object?> scopedContext,
        ExecutionGroupScope? groupScope,
        SelectionSet selectionSet,
        Path selectionPath,
        int executionBranchId,
        DeferUsage deferUsage)
    {
        var contextFactory = parentContext.Services.GetRequiredService<IFactory<OperationContextOwner>>();
        _deferContextOwner = contextFactory.Create();

        // we first need to initialize the rented context for this defer operation.
        _deferContextOwner.OperationContext.InitializeDeferContext(
            parentContext,
            selectionSet,
            selectionPath,
            executionBranchId,
            deferUsage);

        _parent = parent;
        _scopedContext = scopedContext;
        _groupScope = groupScope;
        _executionBranchId = executionBranchId;
        _deferUsage = deferUsage;
        _selectionPath = selectionPath;
    }

    public new void Reset()
    {
        _deferContextOwner.Dispose();
        _deferContextOwner = null!;
        _parent = null!;
        _scopedContext = null!;
        _groupScope = null;
        _executionBranchId = 0;
        _deferUsage = null!;
        _selectionPath = null!;

        base.Reset();
    }

    private ImmutableList<IError> CreateErrors(OperationContext deferContext, Exception exception)
    {
        var errors = ImmutableList.CreateBuilder<IError>();
        var error = ErrorHelper.DeferredFragmentFailed(exception, _selectionPath);
        var handled = deferContext.ErrorHandler.Handle(error);

        if (handled is AggregateError aggregateError)
        {
            errors.AddRange(aggregateError.Errors);
        }
        else
        {
            errors.Add(handled);
        }

        foreach (var handledError in errors)
        {
            deferContext.DiagnosticEvents.TaskError(this, handledError);
        }

        return errors.ToImmutable();
    }
}
