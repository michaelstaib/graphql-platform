using System.Runtime.ExceptionServices;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.ExecutionGroups;
using HotChocolate.Fetching;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// An initialized operation context and work scheduler for an operation whose first root field is an execution
/// group root.
/// </summary>
internal sealed class ExecutionGroupTestHarness
{
    private readonly Action<CancellationToken> _initialize;

    private ExecutionGroupTestHarness(OperationContext operationContext, Action<CancellationToken> initialize)
    {
        OperationContext = operationContext;
        _initialize = initialize;

        var operation = operationContext.Operation;
        var root = operation.RootSelectionSet.Selections[0];
        RootSet = operation.GetSelectionSet(root);
        Plan = RootSet.ExecutionGroupPlan!;
    }

    public OperationContext OperationContext { get; }

    public WorkScheduler Scheduler => OperationContext.Scheduler;

    public SelectionSet RootSet { get; }

    public ExecutionGroupPlan Plan { get; }

    public ConditionFlags IncludeFlags => OperationContext.IncludeConditionFlags;

    public Selection GetSelection(string fieldName)
        => RootSet.Selections.ToArray().Single(s => s.Field.Name == fieldName);

    /// <summary>
    /// Initializes the operation context for another request with the specified abort token.
    /// </summary>
    public void Initialize(CancellationToken requestAborted) => _initialize(requestAborted);

    public static Task<T> RunAsync<T>(string query, Func<ExecutionGroupTestHarness, T> test)
        => RunAsync(query, new Dictionary<string, object?>(), CancellationToken.None, test);

    public static Task<T> RunAsync<T>(
        string query,
        CancellationToken requestAborted,
        Func<ExecutionGroupTestHarness, T> test)
        => RunAsync(query, new Dictionary<string, object?>(), requestAborted, test);

    public static Task<T> RunAsync<T>(
        string query,
        Dictionary<string, object?> variables,
        Func<ExecutionGroupTestHarness, T> test)
        => RunAsync(query, variables, CancellationToken.None, test);

    private static async Task<T> RunAsync<T>(
        string query,
        Dictionary<string, object?> variables,
        CancellationToken requestAborted,
        Func<ExecutionGroupTestHarness, T> test)
    {
        ExceptionDispatchInfo? failure = null;
        var outcome = default(T);

        var executor = await ExecutionGroupTestSchema.CreateAsync(
            new ExecutionScript(),
            builder => builder
                .UseDefaultPipeline()
                .UseRequest(
                    (factoryContext, _) =>
                    {
                        var contextFactory = factoryContext.Services
                            .GetRequiredService<IFactory<OperationContextOwner>>();

                        return context =>
                        {
                            // the owner is abandoned, because tasks that were registered are never executed.
                            var operationContext = contextFactory.Create().OperationContext;

                            void Initialize(CancellationToken aborted)
                                => operationContext.Initialize(
                                    context,
                                    context.RequestServices,
                                    context.RequestServices.GetRequiredService<IBatchDispatcher>(),
                                    context.GetOperation(),
                                    context.VariableValues[0],
                                    rootValue: null,
                                    resolveQueryRootValue: () => null,
                                    requestAbortedOverride: aborted);

                            Initialize(requestAborted);

                            try
                            {
                                outcome = test(new ExecutionGroupTestHarness(operationContext, Initialize));
                            }
                            catch (Exception ex)
                            {
                                failure = ExceptionDispatchInfo.Capture(ex);
                            }

                            context.Result = OperationResult.FromError(
                                ErrorBuilder.New().SetMessage("The test harness stops before execution.").Build());
                            return ValueTask.CompletedTask;
                        };
                    },
                    key: "ExecutionGroupTestHarness",
                    before: WellKnownRequestMiddleware.OperationExecutionMiddleware));

        var request = OperationRequestBuilder.New()
            .SetDocument(query)
            .SetVariableValues(variables)
            .Build();

        await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        failure?.Throw();
        return outcome!;
    }
}
