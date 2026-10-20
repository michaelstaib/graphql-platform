using HotChocolate.Text.Json;
using HotChocolate.Types;

namespace HotChocolate.Execution.Processing.Tasks;

internal static class ExecutionGroupTaskFactory
{
    /// <summary>
    /// Opens an execution group instance for the result of a group root field and enqueues
    /// the tasks below it so that they can find the instance.
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
        var instance = ExecutionGroupInstance.Open(plan, operationContext.IncludeConditionFlags);
        var resolverContext = context.ResolverContext;
        var scopedContext = resolverContext.ScopedContextData;

        resolverContext.ScopedContextData = scopedContext.SetItem(ExecutionGroupInstance.ScopedContextKey, instance);

        try
        {
            ResolverTaskFactory.EnqueueOrInlineResolverTasks(
                context,
                selectionSet,
                selectionSetType,
                resultValue,
                parent);
        }
        catch
        {
            instance.Abandon();
            throw;
        }
        finally
        {
            resolverContext.ScopedContextData = scopedContext;
        }
    }
}
