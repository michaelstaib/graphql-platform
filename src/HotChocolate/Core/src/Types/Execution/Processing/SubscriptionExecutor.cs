using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Options;
using HotChocolate.Types;
using Microsoft.Extensions.ObjectPool;
using static HotChocolate.Execution.ErrorHelper;
using static HotChocolate.Execution.ThrowHelper;

namespace HotChocolate.Execution.Processing;

internal sealed partial class SubscriptionExecutor(
    ObjectPool<OperationContext> operationContextPool,
    QueryExecutor queryExecutor,
    IErrorHandler errorHandler,
    IExecutionDiagnosticEvents diagnosticEvents,
    ExecutionConcurrencyGate? concurrencyGate,
    IRequestTimeoutOptionsAccessor timeoutOptions)
{
    public async Task<IExecutionResult> ExecuteAsync(
        RequestContext requestContext,
        Func<object?> resolveQueryValue)
    {
        ArgumentNullException.ThrowIfNull(requestContext);

        if (requestContext.VariableValues.Length == 0)
        {
            throw SubscriptionExecutor_ContextInvalidState();
        }

        var operation = requestContext.GetOperation();
        var selectionSet = operation.RootSelectionSet;
        if (selectionSet.Selections.Length != 1)
        {
            throw SubscriptionExecutor_SubscriptionsMustHaveOneField();
        }

        if (selectionSet.Selections[0].Field.SubscribeResolver is null)
        {
            throw SubscriptionExecutor_NoSubscribeResolver();
        }

        Subscription? subscription = null;

        try
        {
            if (operation.HasIncrementalParts
                && TryFindEnabledIncrementalDirective(
                    operation,
                    requestContext.VariableValues[0],
                    out var incrementalDirective))
            {
                return DeferAndStreamEnabledInSubscription(incrementalDirective);
            }

            subscription = await Subscription.SubscribeAsync(
                operationContextPool,
                queryExecutor,
                requestContext,
                operation.RootType,
                selectionSet,
                resolveQueryValue,
                diagnosticEvents,
                concurrencyGate,
                timeoutOptions.ExecutionTimeout)
                .ConfigureAwait(false);

            // the subscription setup is complete and nothing writes into the request memory
            // anymore; each event rents its own arena. Sealing the request memory here allows
            // its pages to be returned to the pool once the subscription is disposed. If the
            // setup fails we never get here and the unsealed memory is abandoned instead.
            requestContext.Memory?.Seal();

            var response = new ResponseStream(() => subscription.ExecuteAsync());
            response.ContextData = response.ContextData.SetItem(WellKnownContextData.Subscription, subscription);
            response.RegisterForCleanup(subscription);
            return response;
        }
        catch (GraphQLException ex)
        {
            if (subscription is not null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }

            return new OperationResult([.. ex.Errors]);
        }
        catch (Exception ex)
        {
            var error = errorHandler.Handle(ErrorBuilder.FromException(ex).Build());

            if (subscription is not null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }

            return new OperationResult(Unwrap(error));
        }

        static ImmutableList<IError> Unwrap(IError error)
        {
            if (error is AggregateError aggregateError)
            {
                return [.. aggregateError.Errors];
            }

            return [error];
        }
    }

    private static bool TryFindEnabledIncrementalDirective(
        Operation operation,
        IVariableValueCollection variables,
        [NotNullWhen(true)] out string? directiveName)
    {
        var deferFlags = operation.CreateDeferConditionFlags(variables);
        var accountedDeferConditions = new bool[operation.DeferConditionCount];

        if (TryFindEnabledIncrementalDirective(
            operation,
            operation.RootSelectionSet,
            variables,
            operation.CreateIncludeConditionFlags(variables),
            deferFlags,
            isReachable: true,
            accountedDeferConditions,
            out directiveName))
        {
            return true;
        }

        for (var i = 0; i < accountedDeferConditions.Length; i++)
        {
            if (!accountedDeferConditions[i] && IsConditionEnabled(deferFlags, i))
            {
                directiveName = DirectiveNames.Defer.Name;
                return true;
            }
        }

        directiveName = null;
        return false;
    }

    private static bool TryFindEnabledIncrementalDirective(
        Operation operation,
        SelectionSet selectionSet,
        IVariableValueCollection variables,
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        bool isReachable,
        bool[] accountedDeferConditions,
        [NotNullWhen(true)] out string? directiveName)
    {
        foreach (var selection in selectionSet.Selections)
        {
            var isIncluded = isReachable && selection.IsIncluded(includeFlags);
            var activeDeferUsages = selection.GetActiveDeferUsages(deferFlags);

            if (activeDeferUsages is not null)
            {
                foreach (var activeDeferUsage in activeDeferUsages)
                {
                    for (var usage = activeDeferUsage; usage is not null; usage = usage.Parent)
                    {
                        accountedDeferConditions[usage.DeferConditionIndex] = true;
                    }
                }
            }

            if (isIncluded && (activeDeferUsages is not null || selection.IsDeferred(deferFlags)))
            {
                directiveName = DirectiveNames.Defer.Name;
                return true;
            }

            if (isIncluded && IsStreamEnabled(selection, variables))
            {
                directiveName = DirectiveNames.Stream.Name;
                return true;
            }

            if (selection.IsLeaf)
            {
                continue;
            }

            foreach (var possibleType in operation.GetPossibleTypes(selection))
            {
                if (TryFindEnabledIncrementalDirective(
                    operation,
                    operation.GetSelectionSet(selection, possibleType),
                    variables,
                    includeFlags,
                    deferFlags,
                    isIncluded,
                    accountedDeferConditions,
                    out directiveName))
                {
                    return true;
                }
            }
        }

        directiveName = null;
        return false;
    }

    private static bool IsConditionEnabled(ConditionFlags flags, int index)
    {
        if (index < 64)
        {
            return (flags.Word0 & (1ul << index)) != 0;
        }

        var word = (index >> 6) - 1;

        return flags.Overflow is { } overflow
            && (uint)word < (uint)overflow.Length
            && (overflow[word] & (1ul << (index & 63))) != 0;
    }

    private static bool IsStreamEnabled(Selection selection, IVariableValueCollection variables)
    {
        foreach (var syntaxNode in selection.SyntaxNodes)
        {
            var directive = syntaxNode.Node.GetStreamDirective();

            if (directive is not null
                && DirectiveIfCondition.IsEnabled(
                    DirectiveNames.Stream.Name,
                    directive.GetArgumentValue(DirectiveNames.Stream.Arguments.If),
                    variables,
                    defaultValue: true))
            {
                return true;
            }
        }

        return false;
    }
}
