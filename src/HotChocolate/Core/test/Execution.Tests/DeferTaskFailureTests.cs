using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Processing;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class DeferTaskFailureTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Defer_Should_CompleteTheFragmentWithErrors_When_TheBranchFailsAfterItsWorkSettled()
    {
        // arrange
        var contexts = new RecordingContextOwnerFactory.Contexts();
        var initialDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = await CreateExecutorAsync(contexts, initialDelivered.Task);

        // act
        var payloads = await ReadPayloadsAsync(executor, "{ root ... @defer { deferred } }", initialDelivered);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "root": "root"
              },
              "pending": [
                {
                  "id": "2",
                  "path": []
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "completed": [
                {
                  "id": "2",
                  "errors": [
                    {
                      "message": "Unexpected Execution Error",
                      "path": []
                    }
                  ]
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    private static async Task<List<string>> ReadPayloadsAsync(
        IRequestExecutor executor,
        string query,
        TaskCompletionSource initialDelivered)
    {
        return await Task.Run(
                async () =>
                {
                    var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    var payloads = new List<string>();

                    await foreach (var payload in Assert.IsType<ResponseStream>(result)
                        .ReadResultsAsync()
                        .WithCancellation(TestContext.Current.CancellationToken))
                    {
                        await using var cleanup = payload;
                        payloads.Add(payload.ToJson());
                        initialDelivered.TrySetResult();
                    }

                    return payloads;
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(
        RecordingContextOwnerFactory.Contexts contexts,
        Task initialDelivered)
    {
        var builder = new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor =>
                {
                    descriptor.Field("root").Type<StringType>().Resolve("root");
                    descriptor
                        .Field("deferred")
                        .Type<StringType>()
                        .Resolve(async _ =>
                        {
                            await initialDelivered.ConfigureAwait(false);

                            // the build of the deferred result runs after this resolver settled.
                            contexts.Last.Result.NonNullViolations = null!;
                            return "deferred";
                        });
                })
            .ModifyOptions(options => options.EnableDefer = true);

        builder.Services.AddSingleton<IFactory<OperationContextOwner>>(
            sp => new RecordingContextOwnerFactory(
                new OperationContextOwnerFactory(sp.GetRequiredService<ObjectPool<OperationContext>>()),
                contexts));

        return builder.BuildRequestExecutorAsync();
    }

    private sealed class RecordingContextOwnerFactory(
        IFactory<OperationContextOwner> inner,
        RecordingContextOwnerFactory.Contexts contexts)
        : IFactory<OperationContextOwner>
    {
        public OperationContextOwner Create()
        {
            var owner = inner.Create();
            contexts.Add(owner.OperationContext);
            return owner;
        }

        public sealed class Contexts
        {
            private readonly List<OperationContext> _contexts = [];

            public OperationContext Last
            {
                get
                {
                    lock (_contexts)
                    {
                        return _contexts[^1];
                    }
                }
            }

            public void Add(OperationContext context)
            {
                lock (_contexts)
                {
                    _contexts.Add(context);
                }
            }
        }
    }
}
