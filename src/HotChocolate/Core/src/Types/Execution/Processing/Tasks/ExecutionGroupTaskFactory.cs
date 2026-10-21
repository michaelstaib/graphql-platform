using HotChocolate.Text.Json;
using HotChocolate.Types;

namespace HotChocolate.Execution.Processing.Tasks;

internal static class ExecutionGroupTaskFactory
{
    /// <summary>
    /// Opens an execution group scope for the result of a group root field and enqueues
    /// the tasks below it so that they join the scope.
    /// </summary>
    public static void EnqueueOrInlineResolverTasks(
        ValueCompletionContext context,
        Selection root,
        SelectionSet selectionSet,
        ObjectType selectionSetType,
        ResultElement resultValue,
        object parent)
    {
        var operationContext = context.OperationContext;
        var plan = operationContext.Operation.GetExecutionGroupPlan(root, selectionSet);
        var scope = operationContext.Scheduler.OpenExecutionGroupScope(plan, operationContext.IncludeConditionFlags);
        var groupContext =
            new ValueCompletionContext(
                operationContext,
                context.ResolverContext,
                context.Tasks,
                context.ParentBranchId,
                scope);

        try
        {
            ResolverTaskFactory.EnqueueOrInlineResolverTasks(
                groupContext,
                selectionSet,
                selectionSetType,
                resultValue,
                parent);
        }
        catch
        {
            scope?.Abandon();
            throw;
        }
    }
}
