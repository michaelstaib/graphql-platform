using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class DeferredBatchResolverTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ExecuteAsync_Should_ResolveTheBatchField_When_ItIsTheTopLevelFieldOfADeferredFragment()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ plain ... @defer { batchField } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"plain":"plain"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            incremental {"id":"2","data":{"batchField":"batched-1"}}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheDeferredFragmentHasOnlyABatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ ... @defer { batchField } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            incremental {"id":"2","data":{"batchField":"batched-1"}}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResolveTheBatchFieldsTogether_When_TheDeferredFragmentHasPlainSiblings()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ ... @defer { plain batchField otherBatchField } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            incremental {"id":"2","data":{"plain":"plain","batchField":"batched-1","otherBatchField":"other-1"}}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResolveOneBatchAcrossTheItems_When_EveryItemDefersABatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ items { id ... @defer { batchField } } }");

        // assert
        // the deferred items complete in any order, so the events are compared in sorted order.
        Array.Sort(events, StringComparer.Ordinal);
        events.MatchInlineSnapshots(
        [
            """
            completed {"id":"2"}
            """,
            """
            completed {"id":"3"}
            """,
            """
            completed {"id":"4"}
            """,
            """
            data {"items":[{"id":1},{"id":2},{"id":3}]}
            """,
            """
            hasNext false
            """,
            """
            incremental {"id":"2","data":{"batchField":"batched-3"}}
            """,
            """
            incremental {"id":"3","data":{"batchField":"batched-3"}}
            """,
            """
            incremental {"id":"4","data":{"batchField":"batched-3"}}
            """,
            """
            pending {"id":"2","path":["items",0]}
            """,
            """
            pending {"id":"3","path":["items",1]}
            """,
            """
            pending {"id":"4","path":["items",2]}
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResolveTheBatchFieldOfTheItems_When_TheItemsAreStreamed()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ streamedItems @stream(initialCount: 0) { id batchField } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"streamedItems":[]}
            """,
            """
            pending {"id":"2","path":["streamedItems"]}
            """,
            """
            incremental {"id":"2","items":[{"id":1,"batchField":"batched-3"},{"id":2,"batchField":"batched-3"},{"id":3,"batchField":"batched-3"}]}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReportTheErrorInTheIncrementalEntry_When_TheDeferredBatchFieldFails()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ plain ... @defer { failingBatch } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"plain":"plain"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            incremental {"id":"2","errors":[{"message":"The batch failed.","path":["failingBatch"]}],"data":{"failingBatch":null}}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheDeferredFragmentWithErrors_When_TheNonNullBatchFieldFails()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ plain ... @defer { nonNullFailingBatch } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"plain":"plain"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            completed {"id":"2","errors":[{"message":"The batch failed.","path":["nonNullFailingBatch"]}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteEveryDeferredItem_When_TheNonNullBatchFieldOfTheItemsFails()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ items { id ... @defer { nonNullFailingBatch } } }");

        // assert
        // the deferred items complete in any order, so the events are compared in sorted order.
        Array.Sort(events, StringComparer.Ordinal);
        events.MatchInlineSnapshots(
        [
            """
            completed {"id":"2","errors":[{"message":"The batch failed.","path":["items",0,"nonNullFailingBatch"]}]}
            """,
            """
            completed {"id":"3","errors":[{"message":"The batch failed.","path":["items",1,"nonNullFailingBatch"]}]}
            """,
            """
            completed {"id":"4","errors":[{"message":"The batch failed.","path":["items",2,"nonNullFailingBatch"]}]}
            """,
            """
            data {"items":[{"id":1},{"id":2},{"id":3}]}
            """,
            """
            hasNext false
            """,
            """
            pending {"id":"2","path":["items",0]}
            """,
            """
            pending {"id":"3","path":["items",1]}
            """,
            """
            pending {"id":"4","path":["items",2]}
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReportTheErrorInTheStreamedItems_When_TheBatchFieldOfTheItemsFails()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ streamedItems @stream(initialCount: 0) { id failingBatch } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"streamedItems":[]}
            """,
            """
            pending {"id":"2","path":["streamedItems"]}
            """,
            """
            incremental {"id":"2","errors":[{"message":"The batch failed.","path":["streamedItems",0,"failingBatch"]},{"message":"The batch failed.","path":["streamedItems",1,"failingBatch"]},{"message":"The batch failed.","path":["streamedItems",2,"failingBatch"]}],"items":[{"id":1,"failingBatch":null},{"id":2,"failingBatch":null},{"id":3,"failingBatch":null}]}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteEveryRequest_When_DeferredBatchFieldsRunConcurrently()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var requests = Enumerable.Range(0, 16).Select(_ => Task.Run(
            async () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    await ExecuteIncrementalAsync(executor, "{ plain ... @defer { batchField } }");
                    await ExecuteIncrementalAsync(
                        executor,
                        "{ streamedItems @stream(initialCount: 0) { id batchField } }");
                }
            },
            TestContext.Current.CancellationToken));

        await Task.WhenAll(requests);

        // assert
        var events = await ExecuteIncrementalAsync(executor, "{ plain ... @defer { batchField } }");
        Assert.Equal("hasNext false", events[^1]);
    }

    private static Task<string[]> ExecuteIncrementalAsync(IRequestExecutor executor, string query)
    {
        return Task.Run(
                async () =>
                {
                    await using var result = await executor.ExecuteAsync(
                        query,
                        TestContext.Current.CancellationToken);
                    var events = new List<string>();
                    var hasNext = true;

                    await foreach (var payload in result
                        .ExpectResponseStream()
                        .ReadResultsAsync()
                        .WithCancellation(TestContext.Current.CancellationToken))
                    {
                        await using var payloadCleanup = payload;
                        using var document = JsonDocument.Parse(payload.ToJson(withIndentations: false));
                        var root = document.RootElement;

                        // the events are listed in delivery order without payload boundaries, so the
                        // snapshot does not depend on how the payloads are batched.
                        AddEvents(events, root, "errors", static e => $"errors {e.GetRawText()}");
                        AddEvents(events, root, "data", static d => $"data {d.GetRawText()}");
                        AddEvents(events, root, "pending", static p => $"pending {p.GetRawText()}");
                        AddEvents(events, root, "incremental", static i => $"incremental {i.GetRawText()}");
                        AddEvents(events, root, "completed", static c => $"completed {c.GetRawText()}");

                        hasNext = root.GetProperty("hasNext").GetBoolean();
                    }

                    events.Add($"hasNext {hasNext.ToString().ToLowerInvariant()}");
                    return events.ToArray();
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static void AddEvents(
        List<string> events,
        JsonElement root,
        string propertyName,
        Func<JsonElement, string> format)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return;
        }

        if (value.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                events.Add(format(item));
            }
        }
        else
        {
            events.Add(format(value));
        }
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("plain").Type<StringType>().Resolve("plain");
                d.Field("batchField").Type<StringType>().ResolveBatch(ResolveBatch);
                d.Field("failingBatch").Type<StringType>().ResolveBatch(ResolveFailingBatch);
                d.Field("nonNullFailingBatch").Type<NonNullType<StringType>>().ResolveBatch(ResolveFailingBatch);
                d.Field("otherBatchField")
                    .Type<StringType>()
                    .ResolveBatch(contexts => ResolveBatch(contexts, "other"));
                d.Field("items")
                    .Type<ListType<ObjectType<Item>>>()
                    .Resolve(_ => new[] { new Item(1), new Item(2), new Item(3) });
                d.Field("streamedItems")
                    .Type<ListType<ObjectType<Item>>>()
                    .Resolve(_ => StreamItemsAsync());
            })
            .AddObjectType<Item>(d =>
            {
                d.Field(t => t.Id);
                d.Field("batchField").Type<StringType>().ResolveBatch(ResolveBatch);
                d.Field("failingBatch").Type<StringType>().ResolveBatch(ResolveFailingBatch);
                d.Field("nonNullFailingBatch").Type<NonNullType<StringType>>().ResolveBatch(ResolveFailingBatch);
            })
            .ModifyOptions(
                o =>
                {
                    o.EnableDefer = true;
                    o.EnableStream = true;
                })
            .BuildRequestExecutorAsync();

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatch(IReadOnlyList<IResolverContext> contexts)
        => ResolveBatch(contexts, "batched");

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatch(
        IReadOnlyList<IResolverContext> contexts,
        string prefix)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok($"{prefix}-{contexts.Count}");
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveFailingBatch(
        IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Fail(ErrorBuilder.New().SetMessage("The batch failed.").Build());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    private static async IAsyncEnumerable<Item> StreamItemsAsync()
    {
        await Task.CompletedTask;
        yield return new Item(1);
        yield return new Item(2);
        yield return new Item(3);
    }

    public sealed record Item(int Id);
}
