using System.Collections.Concurrent;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class BatchPathDrainTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ExecuteAsync_Should_DispatchTheBatch_When_ItsPathDrainsWhileSiblingWorkStillRuns()
    {
        // arrange
        var events = new ConcurrentQueue<string>();
        var batchDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("sibling")
                    .Type<StringType>()
                    .Resolve(async _ =>
                    {
                        events.Enqueue("sibling:start");
                        await batchDispatched.Task.WaitAsync(s_timeout);
                        events.Enqueue("sibling:end");
                        return "sibling";
                    });
                d.Field("foo").Type<ObjectType<Foo>>().Resolve(new Foo());
            })
            .AddObjectType<Foo>(d =>
                d.Field("batch")
                    .Type<StringType>()
                    .ResolveBatch(contexts =>
                    {
                        events.Enqueue($"batch:start({contexts.Count})");
                        batchDispatched.TrySetResult();

                        var results = new ResolverResult[contexts.Count];

                        for (var i = 0; i < results.Length; i++)
                        {
                            results[i] = ResolverResult.Ok("batched");
                        }

                        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
                    }))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var json = await ExecuteAsync(executor, "{ sibling foo { batch } }");

        // assert
        events.ToArray().MatchInlineSnapshots(
        [
            """
            sibling:start
            """,
            """
            batch:start(1)
            """,
            """
            sibling:end
            """
        ]);
        json.MatchInlineSnapshot(
            """
            {"data":{"sibling":"sibling","foo":{"batch":"batched"}}}
            """);
    }

    private static Task<string> ExecuteAsync(IRequestExecutor executor, string query)
    {
        // the scheduler loop must not be able to block the test thread, so the request runs off it.
        return Task.Run(
                async () =>
                {
                    var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    return result.ToJson(withIndentations: false);
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    public sealed class Foo
    {
        public string Name => "foo";
    }
}
