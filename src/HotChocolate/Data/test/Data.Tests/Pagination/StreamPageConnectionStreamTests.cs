using System.Text.Json.Nodes;
using CookieCrumble.Resources;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using static HotChocolate.Data.Pagination.StreamPageConnectionDataRequirementTests;

namespace HotChocolate.Data.Pagination;

[Collection(PostgresCacheCollectionFixture.DefinitionName)]
public class StreamPageConnectionStreamTests(PostgreSqlResource resource)
{
    [Fact]
    public async Task Execute_Should_StreamEdges_When_InitialCountIsZero()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { edges @stream(initialCount: 0) { cursor } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { edges @stream(initialCount: 0) { cursor } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"edges":[]}}""",
            """completed ["items","edges"]""",
            """stream ["items","edges"] [{"cursor":"e30x"},{"cursor":"e30y"}]"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"edges":[]}},{"id":2,"items":{"edges":[]}}]}""",
            """completed ["authors",0,"items","edges"]""",
            """completed ["authors",1,"items","edges"]""",
            """stream ["authors",0,"items","edges"] [{"cursor":"e30x"},{"cursor":"e30y"}]""",
            """stream ["authors",1,"items","edges"] [{"cursor":"e302"},{"cursor":"e303"}]"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_StreamNodes_When_InitialCountIsGreaterThanZero()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 3) { nodes @stream(initialCount: 2) { id } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 3) { nodes @stream(initialCount: 2) { id } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1},{"id":2}]}}""",
            """completed ["items","nodes"]""",
            """stream ["items","nodes"] [{"id":3}]"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1},{"id":2}]}},{"id":2,"items":{"nodes":[{"id":6},{"id":7}]}}]}""",
            """completed ["authors",0,"items","nodes"]""",
            """completed ["authors",1,"items","nodes"]""",
            """stream ["authors",0,"items","nodes"] [{"id":3}]""",
            """stream ["authors",1,"items","nodes"] [{"id":8}]"""
        ]);
        store.Summarize().MatchInlineSnapshot(
            """
            {
              "RowsWereRead": true,
              "ReadAfterResolver": true
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_StreamEdgesAndNodes_When_BothAreStreamed()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(
            executor,
            "{ items(first: 3) { edges @stream(initialCount: 1) { cursor } nodes @stream(initialCount: 2) { id } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 3) { edges @stream(initialCount: 1) { cursor } nodes @stream(initialCount: 2) { id } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"edges":[{"cursor":"e30x"}],"nodes":[{"id":1},{"id":2}]}}""",
            """completed ["items","edges"]""",
            """completed ["items","nodes"]""",
            """stream ["items","edges"] [{"cursor":"e30y"},{"cursor":"e30z"}]""",
            """stream ["items","nodes"] [{"id":3}]"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"edges":[{"cursor":"e30x"}],"nodes":[{"id":1},{"id":2}]}},{"id":2,"items":{"edges":[{"cursor":"e302"}],"nodes":[{"id":6},{"id":7}]}}]}""",
            """completed ["authors",0,"items","edges"]""",
            """completed ["authors",0,"items","nodes"]""",
            """completed ["authors",1,"items","edges"]""",
            """completed ["authors",1,"items","nodes"]""",
            """stream ["authors",0,"items","edges"] [{"cursor":"e30y"},{"cursor":"e30z"}]""",
            """stream ["authors",0,"items","nodes"] [{"id":3}]""",
            """stream ["authors",1,"items","edges"] [{"cursor":"e303"},{"cursor":"e304"}]""",
            """stream ["authors",1,"items","nodes"] [{"id":8}]"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_StreamNodes_When_PageInfoIsDeferredAsAWhole()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(
            executor,
            "{ items(first: 2) { nodes @stream(initialCount: 1) { id } ... @defer { pageInfo { startCursor hasNextPage endCursor } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes @stream(initialCount: 1) { id } ... @defer { pageInfo { startCursor hasNextPage endCursor } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1}]}}""",
            """completed ["items","nodes"]""",
            """completed ["items"]""",
            """defer ["items"] {"pageInfo":{"startCursor":"e30x","hasNextPage":true,"endCursor":"e30y"}}""",
            """stream ["items","nodes"] [{"id":2}]"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1}]}},{"id":2,"items":{"nodes":[{"id":6}]}}]}""",
            """completed ["authors",0,"items","nodes"]""",
            """completed ["authors",0,"items"]""",
            """completed ["authors",1,"items","nodes"]""",
            """completed ["authors",1,"items"]""",
            """defer ["authors",0,"items"] {"pageInfo":{"startCursor":"e30x","hasNextPage":true,"endCursor":"e30y"}}""",
            """defer ["authors",1,"items"] {"pageInfo":{"startCursor":"e302","hasNextPage":true,"endCursor":"e303"}}""",
            """stream ["authors",0,"items","nodes"] [{"id":2}]""",
            """stream ["authors",1,"items","nodes"] [{"id":7}]"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_StreamNodes_When_PageInfoFieldsAreDeferredSeparately()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(
            executor,
            "{ items(first: 2) { nodes @stream(initialCount: 1) { id } pageInfo { startCursor ... @defer(label: \"a\") { hasNextPage } ... @defer(label: \"b\") { endCursor } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes @stream(initialCount: 1) { id } pageInfo { startCursor ... @defer(label: \"a\") { hasNextPage } ... @defer(label: \"b\") { endCursor } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1}],"pageInfo":{"startCursor":"e30x"}}}""",
            """completed ["items","nodes"]""",
            """completed ["items","pageInfo"] [a]""",
            """completed ["items","pageInfo"] [b]""",
            """defer ["items","pageInfo"] [a] {"hasNextPage":true}""",
            """defer ["items","pageInfo"] [b] {"endCursor":"e30y"}""",
            """stream ["items","nodes"] [{"id":2}]"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1}],"pageInfo":{"startCursor":"e30x"}}},{"id":2,"items":{"nodes":[{"id":6}],"pageInfo":{"startCursor":"e302"}}}]}""",
            """completed ["authors",0,"items","nodes"]""",
            """completed ["authors",0,"items","pageInfo"] [a]""",
            """completed ["authors",0,"items","pageInfo"] [b]""",
            """completed ["authors",1,"items","nodes"]""",
            """completed ["authors",1,"items","pageInfo"] [a]""",
            """completed ["authors",1,"items","pageInfo"] [b]""",
            """defer ["authors",0,"items","pageInfo"] [a] {"hasNextPage":true}""",
            """defer ["authors",0,"items","pageInfo"] [b] {"endCursor":"e30y"}""",
            """defer ["authors",1,"items","pageInfo"] [a] {"hasNextPage":true}""",
            """defer ["authors",1,"items","pageInfo"] [b] {"endCursor":"e303"}""",
            """stream ["authors",0,"items","nodes"] [{"id":2}]""",
            """stream ["authors",1,"items","nodes"] [{"id":7}]"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_DeliverTotalCountWithTheConnection_When_NodesAreStreamed()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(
            executor,
            "{ items(first: 2) { totalCount nodes @stream(initialCount: 1) { id } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { totalCount nodes @stream(initialCount: 1) { id } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"totalCount":10,"nodes":[{"id":1}]}}""",
            """completed ["items","nodes"]""",
            """stream ["items","nodes"] [{"id":2}]"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"totalCount":5,"nodes":[{"id":1}]}},{"id":2,"items":{"totalCount":5,"nodes":[{"id":6}]}}]}""",
            """completed ["authors",0,"items","nodes"]""",
            """completed ["authors",1,"items","nodes"]""",
            """stream ["authors",0,"items","nodes"] [{"id":2}]""",
            """stream ["authors",1,"items","nodes"] [{"id":7}]"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_IgnoreStream_When_OnlyOneOfEdgesAndNodesIsStreamed()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(
            executor,
            "{ items(first: 2) { edges { cursor } nodes @stream(initialCount: 1) { id } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { edges { cursor } nodes @stream(initialCount: 1) { id } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"edges":[{"cursor":"e30x"},{"cursor":"e30y"}],"nodes":[{"id":1},{"id":2}]}}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"edges":[{"cursor":"e30x"},{"cursor":"e30y"}],"nodes":[{"id":1},{"id":2}]}},{"id":2,"items":{"edges":[{"cursor":"e302"},{"cursor":"e303"}],"nodes":[{"id":6},{"id":7}]}}]}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_IgnoreStream_When_AMemberThatIsNotStreamableIsDeliveredWithTheConnection()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(
            executor,
            "{ items(first: 2) { nodes @stream(initialCount: 1) { id } pageInfo { hasNextPage } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes @stream(initialCount: 1) { id } pageInfo { endCursor } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1},{"id":2}],"pageInfo":{"hasNextPage":true}}}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1},{"id":2}],"pageInfo":{"endCursor":"e30y"}}},{"id":2,"items":{"nodes":[{"id":6},{"id":7}],"pageInfo":{"endCursor":"e303"}}}]}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_IgnoreStream_When_TheStreamConditionIsFalse()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 1, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var result = await ReadAsync(
            executor,
            "query($s: Boolean!) { items(first: 2) { edges @stream(if: $s, initialCount: 0) { cursor } nodes @stream(if: $s) { id } } }",
            new Dictionary<string, object?> { ["s"] = false });

        // assert
        result.MatchInlineSnapshots(
        [
            """initial {"items":{"edges":[{"cursor":"e30x"},{"cursor":"e30y"}],"nodes":[{"id":1},{"id":2}]}}"""
        ]);
        store.Summarize().MatchInlineSnapshot(
            """
            {
              "RowsWereRead": true,
              "ReadAfterResolver": false
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_DeliverTheInitialPayload_When_TheStreamIsStillReading()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 1, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        store.HoldReadsAfterResolverReturns(1);

        try
        {
            // act
            await using var result = await executor.ExecuteAsync(
                "{ items(first: 3) { edges @stream(initialCount: 1) { cursor } nodes @stream(initialCount: 1) { id } ... @defer { pageInfo { hasNextPage } } } }",
                cts.Token);
            var initial = string.Empty;
            var pending = new Dictionary<string, string>();
            var completed = new List<string>();

            await foreach (var payload in result.ExpectResponseStream().ReadResultsAsync().WithCancellation(cts.Token))
            {
                await using var cleanup = payload;
                var root = JsonNode.Parse(payload.ToJson())!;

                if (root["data"] is { } data)
                {
                    // the reads of the streams are held until the initial payload has arrived.
                    initial = data.ToJsonString();
                    store.ReleaseReads();
                }

                foreach (var entry in root["pending"]?.AsArray() ?? [])
                {
                    pending[entry!["id"]!.GetValue<string>()] = entry["path"]!.ToJsonString();
                }

                foreach (var entry in root["completed"]?.AsArray() ?? [])
                {
                    completed.Add(pending[entry!["id"]!.GetValue<string>()]);
                }
            }

            // assert
            initial.MatchInlineSnapshot("""{"items":{"edges":[{"cursor":"e30x"}],"nodes":[{"id":1}]}}""");
            completed.MatchInlineSnapshots(
            [
                """["items","edges"]""",
                """["items","nodes"]""",
                """["items"]"""
            ]);
        }
        finally
        {
            store.ReleaseReads();
        }
    }

    private static async Task<List<string>> ReadAsync(
        IRequestExecutor executor,
        string query,
        Dictionary<string, object?>? variables = null)
    {
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New()
                .SetDocument(query)
                .SetVariableValues(variables ?? [])
                .Build(),
            TestContext.Current.CancellationToken);
        var pending = new Dictionary<string, string>();
        var streamed = new Dictionary<string, JsonArray>();
        var deferred = new List<string>();
        var completed = new List<string>();
        var errors = new List<string>();
        var initial = string.Empty;

        if (result is IResponseStream stream)
        {
            await foreach (var payload in stream.ReadResultsAsync())
            {
                var root = JsonNode.Parse(payload.ToJson())!;
                await payload.DisposeAsync();

                initial = root["data"] is { } data ? data.ToJsonString() : initial;

                foreach (var entry in root["pending"]?.AsArray() ?? [])
                {
                    var label = entry!["label"] is { } l ? $" [{l}]" : string.Empty;
                    pending[entry["id"]!.GetValue<string>()] = entry["path"]!.ToJsonString() + label;
                }

                foreach (var entry in root["incremental"]?.AsArray() ?? [])
                {
                    var id = entry!["id"]!.GetValue<string>();

                    if (entry["items"] is JsonArray items)
                    {
                        if (!streamed.TryGetValue(id, out var all))
                        {
                            all = [];
                            streamed.Add(id, all);
                        }

                        foreach (var item in items)
                        {
                            all.Add(item!.DeepClone());
                        }
                    }
                    else
                    {
                        deferred.Add($"defer {pending[id]} {entry["data"]!.ToJsonString()}");
                    }
                }

                foreach (var entry in root["completed"]?.AsArray() ?? [])
                {
                    var completedErrors = entry!["errors"] is { } e ? $" {e.ToJsonString()}" : string.Empty;
                    completed.Add($"completed {pending[entry["id"]!.GetValue<string>()]}{completedErrors}");
                }

                foreach (var error in root["errors"]?.AsArray() ?? [])
                {
                    errors.Add(error!.ToJsonString());
                }
            }
        }
        else
        {
            var root = JsonNode.Parse(result.ToJson())!;
            initial = root["data"]!.ToJsonString();

            foreach (var error in root["errors"]?.AsArray() ?? [])
            {
                errors.Add(error!.ToJsonString());
            }
        }

        await result.DisposeAsync();

        var incremental = streamed
            .Select(t => $"stream {pending[t.Key]} {t.Value.ToJsonString()}")
            .Concat(deferred)
            .Concat(completed)
            .Order(StringComparer.Ordinal);

        return [$"initial {initial}", .. incremental, .. errors.Select(e => $"error {e}")];
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync(Store store)
        => await new ServiceCollection()
            .AddSingleton(store)
            .AddGraphQLServer()
            .AddQueryType<QueryType>()
            .AddType<AuthorType>()
            .AddType<ItemConnectionType>()
            .AddDataLoader<ItemsByAuthorDataLoader>()
            .AddPagingArguments()
            .ModifyOptions(options =>
            {
                options.EnableDefer = true;
                options.EnableStream = true;
            })
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
}
