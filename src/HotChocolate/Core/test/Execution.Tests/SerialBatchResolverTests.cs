using System.Collections.Concurrent;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class SerialBatchResolverTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ExecuteAsync_Should_RunBatchRootFieldsOneAtATime_When_TheOperationIsAMutation()
    {
        // arrange
        var events = new ConcurrentQueue<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d => d.Name("Query").Field("noop").Type<StringType>().Resolve("noop"))
            .AddMutationType(d =>
            {
                d.Name("Mutation");
                d.Field("first")
                    .Type<StringType>()
                    .ResolveBatch(contexts => ResolveGatedAsync("first", contexts, events, firstStarted, firstGate));
                d.Field("second")
                    .Type<StringType>()
                    .ResolveBatch(contexts => ResolveGatedAsync("second", contexts, events, null, secondGate));
            })
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        secondGate.SetResult();

        // act
        var execution = ExecuteAsync(executor, "mutation { first second }");
        await firstStarted.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        firstGate.SetResult();
        var json = await execution;

        // assert
        events.ToArray().MatchInlineSnapshots(
        [
            """
            first:start(1)
            """,
            """
            first:end
            """,
            """
            second:start(1)
            """,
            """
            second:end
            """
        ]);
        json.MatchInlineSnapshot(
            """
            {"data":{"first":"first-1","second":"second-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RunBatchResolverOncePerParent_When_TheFieldIsSerial()
    {
        // arrange
        var executor = await CreateItemsExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ items { serialBatch } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"items":[{"serialBatch":"batched-1"},{"serialBatch":"batched-1"},{"serialBatch":"batched-1"}]}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_BatchAcrossParents_When_TheFieldIsNotSerial()
    {
        // arrange
        var executor = await CreateItemsExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ items { batch } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"items":[{"batch":"batched-3"},{"batch":"batched-3"},{"batch":"batched-3"}]}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResolveSerialAndBatchFieldsOfTheSameParent_When_BothAreSelected()
    {
        // arrange
        var executor = await CreateItemsExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ items { serialBatch batch } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"items":[{"serialBatch":"batched-1","batch":"batched-3"},{"serialBatch":"batched-1","batch":"batched-3"},{"serialBatch":"batched-1","batch":"batched-3"}]}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReportTheBatchResolverError_When_ASerialBatchFieldFails()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("failing")
                    .Type<StringType>()
                    .Serial()
                    .ResolveBatch(contexts =>
                    {
                        var results = new ResolverResult[contexts.Count];

                        for (var i = 0; i < results.Length; i++)
                        {
                            results[i] = ResolverResult.Fail(ErrorBuilder.New().SetMessage("failed").Build());
                        }

                        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
                    });
            })
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var json = await ExecuteAsync(executor, "{ failing }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"errors":[{"message":"failed","path":["failing"]}],"data":{"failing":null}}
            """);
    }

    private static async ValueTask<IReadOnlyList<ResolverResult>> ResolveGatedAsync(
        string name,
        IReadOnlyList<IResolverContext> contexts,
        ConcurrentQueue<string> events,
        TaskCompletionSource? started,
        TaskCompletionSource gate)
    {
        events.Enqueue($"{name}:start({contexts.Count})");
        started?.TrySetResult();
        await gate.Task.WaitAsync(s_timeout);
        events.Enqueue($"{name}:end");

        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < results.Length; i++)
        {
            results[i] = ResolverResult.Ok($"{name}-{contexts.Count}");
        }

        return results;
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

    private static ValueTask<IRequestExecutor> CreateItemsExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("items")
                    .Type<ListType<ObjectType<Item>>>()
                    .Resolve(_ => new[] { new Item(), new Item(), new Item() });
            })
            .AddObjectType<Item>(d =>
            {
                d.Field("serialBatch").Type<StringType>().Serial().ResolveBatch(ResolveBatch);
                d.Field("batch").Type<StringType>().ResolveBatch(ResolveBatch);
            })
            .BuildRequestExecutorAsync();

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatch(IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok($"batched-{contexts.Count}");
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    public sealed class Item
    {
        public string Name => "item";
    }
}
