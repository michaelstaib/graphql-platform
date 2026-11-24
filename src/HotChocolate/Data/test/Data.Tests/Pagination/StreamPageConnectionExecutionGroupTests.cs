using System.Text.Json.Nodes;
using CookieCrumble.Resources;
using HotChocolate.Execution;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using static HotChocolate.Data.Pagination.StreamPageConnectionDataRequirementTests;

namespace HotChocolate.Data.Pagination;

[Collection(PostgresCacheCollectionFixture.DefinitionName)]
public class StreamPageConnectionExecutionGroupTests(PostgreSqlResource resource)
{
    [Fact]
    public async Task Execute_Should_SequenceGroupMembers_When_NodesAreDeferred()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { ... @defer { nodes { id } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { ... @defer { nodes { id } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{}}""",
            """["items"] {"nodes":[{"id":1},{"id":2}]}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{}},{"id":2,"items":{}}]}""",
            """["authors",0,"items"] {"nodes":[{"id":1},{"id":2}]}""",
            """["authors",1,"items"] {"nodes":[{"id":6},{"id":7}]}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_SequenceGroupMembers_When_TotalCountIsDeferred()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { nodes { id } ... @defer { totalCount } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes { id } ... @defer { totalCount } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1},{"id":2}]}}""",
            """["items"] {"totalCount":10}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1},{"id":2}]}},{"id":2,"items":{"nodes":[{"id":6},{"id":7}]}}]}""",
            """["authors",0,"items"] {"totalCount":5}""",
            """["authors",1,"items"] {"totalCount":5}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_SequenceGroupMembers_When_PageInfoIsDeferredAfterNodes()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { nodes { id } ... @defer { pageInfo { hasNextPage endCursor } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes { id } ... @defer { pageInfo { hasNextPage endCursor } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1},{"id":2}]}}""",
            """["items"] {"pageInfo":{"hasNextPage":true,"endCursor":"e30y"}}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1},{"id":2}]}},{"id":2,"items":{"nodes":[{"id":6},{"id":7}]}}]}""",
            """["authors",0,"items"] {"pageInfo":{"hasNextPage":true,"endCursor":"e30y"}}""",
            """["authors",1,"items"] {"pageInfo":{"hasNextPage":true,"endCursor":"e303"}}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_SequenceGroupMembers_When_PageInfoFieldsAreDeferredSeparately()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { nodes { id } pageInfo { startCursor ... @defer { hasNextPage } ... @defer { endCursor } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes { id } pageInfo { startCursor ... @defer { hasNextPage } ... @defer { endCursor } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1},{"id":2}],"pageInfo":{"startCursor":"e30x"}}}""",
            """["items","pageInfo"] {"hasNextPage":true,"endCursor":"e30y"}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1},{"id":2}],"pageInfo":{"startCursor":"e30x"}}},{"id":2,"items":{"nodes":[{"id":6},{"id":7}],"pageInfo":{"startCursor":"e302"}}}]}""",
            """["authors",0,"items","pageInfo"] {"hasNextPage":true,"endCursor":"e30y"}""",
            """["authors",1,"items","pageInfo"] {"hasNextPage":true,"endCursor":"e303"}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_SequenceGroupMembers_When_WholePageInfoIsDeferred()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { nodes { id } ... @defer { pageInfo { startCursor hasNextPage hasPreviousPage endCursor } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { nodes { id } ... @defer { pageInfo { startCursor hasNextPage hasPreviousPage endCursor } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{"nodes":[{"id":1},{"id":2}]}}""",
            """["items"] {"pageInfo":{"startCursor":"e30x","hasNextPage":true,"hasPreviousPage":false,"endCursor":"e30y"}}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{"nodes":[{"id":1},{"id":2}]}},{"id":2,"items":{"nodes":[{"id":6},{"id":7}]}}]}""",
            """["authors",0,"items"] {"pageInfo":{"startCursor":"e30x","hasNextPage":true,"hasPreviousPage":false,"endCursor":"e30y"}}""",
            """["authors",1,"items"] {"pageInfo":{"startCursor":"e302","hasNextPage":true,"hasPreviousPage":false,"endCursor":"e303"}}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_SequenceGroupMembers_When_NodesAndPageInfoAreDeferredInSeparateFragments()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 2, itemsPerAuthor: 5);
        var executor = await CreateExecutorAsync(store);

        // act
        var single = await ReadAsync(executor, "{ items(first: 2) { ... @defer(label: \"nodes\") { nodes { id } } ... @defer(label: \"info\") { pageInfo { hasNextPage } } } }");
        var batched = await ReadAsync(
            executor,
            "{ authors { id items(first: 2) { ... @defer(label: \"nodes\") { nodes { id } } ... @defer(label: \"info\") { pageInfo { hasNextPage } } } } }");

        // assert
        single.MatchInlineSnapshots(
        [
            """initial {"items":{}}""",
            """["items"] [info] {"pageInfo":{"hasNextPage":true}}""",
            """["items"] [nodes] {"nodes":[{"id":1},{"id":2}]}"""
        ]);
        batched.MatchInlineSnapshots(
        [
            """initial {"authors":[{"id":1,"items":{}},{"id":2,"items":{}}]}""",
            """["authors",0,"items"] [info] {"pageInfo":{"hasNextPage":true}}""",
            """["authors",0,"items"] [nodes] {"nodes":[{"id":1},{"id":2}]}""",
            """["authors",1,"items"] [info] {"pageInfo":{"hasNextPage":true}}""",
            """["authors",1,"items"] [nodes] {"nodes":[{"id":6},{"id":7}]}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_RunGroupMembersOneAtATimeInPriorityOrder_When_AllMembersAreSelected()
    {
        // arrange
        var store = await Store.CreateAsync(resource, authorCount: 1, itemsPerAuthor: 5);
        var gate = new MemberGate();
        var executor = await CreateExecutorAsync(store, gate);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // act
        var execution = executor.ExecuteAsync(
            "{ items(first: 2) { pageInfo { endCursor hasNextPage } nodes { id } edges { cursor } } }",
            cts.Token);
        await gate.StartedAsync("edges");
        var whileEdgesRun = gate.Events;
        gate.Release("edges");
        await gate.StartedAsync("nodes");
        var whileNodesRun = gate.Events;
        gate.Release("nodes");
        await gate.StartedAsync("hasNextPage");
        var whileHasNextPageRuns = gate.Events;
        gate.Release("hasNextPage");
        gate.Release("endCursor");
        await using var result = await execution;

        // assert
        whileEdgesRun.MatchInlineSnapshots(["start edges"]);
        whileNodesRun.MatchInlineSnapshots(["start edges", "end edges", "start nodes"]);
        whileHasNextPageRuns.MatchInlineSnapshots(
        [
            "start edges",
            "end edges",
            "start nodes",
            "end nodes",
            "start hasNextPage"
        ]);
        gate.Events.MatchInlineSnapshots(
        [
            "start edges",
            "end edges",
            "start nodes",
            "end nodes",
            "start hasNextPage",
            "end hasNextPage",
            "start endCursor",
            "end endCursor"
        ]);
        result.ToJson().MatchInlineSnapshot(
            """
            {
              "data": {
                "items": {
                  "pageInfo": {
                    "endCursor": "e30y",
                    "hasNextPage": true
                  },
                  "nodes": [
                    {
                      "id": 1
                    },
                    {
                      "id": 2
                    }
                  ],
                  "edges": [
                    {
                      "cursor": "e30x"
                    },
                    {
                      "cursor": "e30y"
                    }
                  ]
                }
              }
            }
            """);
    }

    private static async Task<List<string>> ReadAsync(IRequestExecutor executor, string query)
    {
        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
        var pending = new Dictionary<string, string>();
        var incremental = new List<string>();
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
                    var content = entry!["data"] ?? entry["items"];
                    incremental.Add($"{pending[entry["id"]!.GetValue<string>()]} {content!.ToJsonString()}");
                }

                foreach (var error in root["errors"]?.AsArray() ?? [])
                {
                    errors.Add(error!.ToJsonString());
                }
            }
        }
        else
        {
            initial = JsonNode.Parse(result.ToJson())!.ToJsonString();
        }

        await result.DisposeAsync();
        incremental.Sort(StringComparer.Ordinal);

        return [$"initial {initial}", .. incremental, .. errors.Select(e => $"error {e}")];
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync(Store store, MemberGate? gate = null)
    {
        var builder = new ServiceCollection()
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
            });

        if (gate is not null)
        {
            builder.UseField(next => context => gate.RunAsync(context, next));
        }

        return await builder.BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class MemberGate
    {
        private static readonly string[] s_members = ["edges", "nodes", "endCursor", "hasNextPage"];
        private readonly object _sync = new();
        private readonly List<string> _events = [];
        private readonly Dictionary<string, TaskCompletionSource> _gates = [];
        private readonly Dictionary<string, TaskCompletionSource> _started = [];

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_sync)
                {
                    return [.. _events];
                }
            }
        }

        public void Release(string member) => Get(_gates, member).TrySetResult();

        public async Task StartedAsync(string member)
        {
            try
            {
                await Get(_started, member).Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"{member} never started: {string.Join(", ", Events)}");
            }
        }

        public async ValueTask RunAsync(IMiddlewareContext context, FieldDelegate next)
        {
            var member = context.Selection.Field.Name;

            if (!s_members.Contains(member))
            {
                await next(context);
                return;
            }

            lock (_sync)
            {
                _events.Add($"start {member}");
            }

            Get(_started, member).TrySetResult();
            await Get(_gates, member).Task.WaitAsync(context.RequestAborted);
            await next(context);

            lock (_sync)
            {
                _events.Add($"end {member}");
            }
        }

        private TaskCompletionSource Get(Dictionary<string, TaskCompletionSource> map, string member)
        {
            lock (_sync)
            {
                if (!map.TryGetValue(member, out var source))
                {
                    source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    map.Add(member, source);
                }

                return source;
            }
        }
    }
}
