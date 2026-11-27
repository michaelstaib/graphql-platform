using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class DeferPendingOrderTests
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    [Fact]
    public async Task Defer_Should_AnnounceFragmentsInFieldOrder_When_NestedFragmentComesBeforeRootFragment()
    {
        // act
        var entries = await ExecuteAsync(
            """
            {
              a { ... @defer(label: "B") { x } }
              ... @defer(label: "A") { a { x } }
            }
            """);

        // assert
        entries.MatchInlineSnapshots(
            [
                """
                {
                  "a": {}
                }
                """,
                """
                {
                  "id": "2",
                  "path": [
                    "a"
                  ],
                  "label": "B",
                  "data": {
                    "x": "x"
                  }
                }
                """,
                """
                {
                  "id": "3",
                  "path": [
                    "a"
                  ],
                  "label": "A",
                  "data": {
                    "x": "x"
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task Defer_Should_AnnounceFragmentsInFieldOrder_When_UnlabelledFragmentIsCreatedFirst()
    {
        // act
        var entries = await ExecuteAsync(
            """
            {
              ... @defer { y @include(if: false) }
              ... @defer(label: "B") { x }
              ... @defer { x }
            }
            """);

        // assert
        entries.MatchInlineSnapshots(
            [
                """
                {}
                """,
                """
                {
                  "id": "2",
                  "path": [],
                  "label": "B",
                  "data": {
                    "x": "qx"
                  }
                }
                """,
                """
                {
                  "id": "3",
                  "path": [],
                  "data": {
                    "x": "qx"
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task Defer_Should_AnnounceFragmentsInFieldOrder_When_ThreeFragmentsShareFields()
    {
        // act
        var entries = await ExecuteAsync(
            """
            {
              a { ... @defer(label: "B") { x } ... @defer(label: "C") { y } }
              ... @defer(label: "A") { a { y x } }
            }
            """);

        // assert
        entries.MatchInlineSnapshots(
            [
                """
                {
                  "a": {}
                }
                """,
                """
                {
                  "id": "2",
                  "path": [
                    "a"
                  ],
                  "label": "B",
                  "data": {
                    "x": "x"
                  }
                }
                """,
                """
                {
                  "id": "3",
                  "path": [
                    "a"
                  ],
                  "label": "A",
                  "data": {
                    "x": "x",
                    "y": "y"
                  }
                }
                """,
                """
                {
                  "id": "4",
                  "path": [
                    "a"
                  ],
                  "label": "C",
                  "data": {
                    "y": "y"
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverOneFragment_When_UnlabelledFragmentsAreSpreadAtDifferentDepths()
    {
        // act
        var entries = await ExecuteAsync(
            """
            {
              ... @defer { a { y } }
              a { ... @defer { x } }
            }
            """);

        // assert
        entries.MatchInlineSnapshots(
            [
                """
                {
                  "a": {}
                }
                """,
                """
                {
                  "id": "2",
                  "path": [
                    "a"
                  ],
                  "data": {
                    "y": "y",
                    "x": "x"
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverInnerFragment_When_EqualFragmentsAreNestedAtDifferentDepths()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var response = await ReadAsync(
            executor,
            """
            {
              ... @defer { a { ... @defer { z } } }
              a { ... @defer { z bad } }
            }
            """);
        var expected = await ReadDataAsync(executor, "{ a { z bad } }");

        // assert
        ToEntries(response).MatchInlineSnapshots(
            [
                """
                {
                  "a": {}
                }
                """,
                """
                {
                  "id": "2",
                  "path": [
                    "a"
                  ],
                  "data": {
                    "z": "z",
                    "bad": "bad"
                  }
                }
                """
            ]);
        Assert.True(JsonNode.DeepEquals(Merge(response), expected));
    }

    private static async Task<List<string>> ExecuteAsync(string query)
    {
        var executor = await CreateExecutorAsync();
        return ToEntries(await ReadAsync(executor, query));
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync()
        => await new ServiceCollection()
            .AddGraphQL()
            .AddDocumentFromString(
                """
                type Query { a: A x: String y: String }
                type A { x: String y: String z: String bad: String }
                """)
            .AddResolver("Query", "a", _ => new object())
            .AddResolver("Query", "x", _ => "qx")
            .AddResolver("Query", "y", _ => "qy")
            .AddResolver("A", "x", _ => "x")
            .AddResolver("A", "y", _ => "y")
            .AddResolver("A", "z", _ => "z")
            .AddResolver("A", "bad", _ => "bad")
            .ModifyOptions(o => o.EnableDefer = true)
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static async Task<JsonNode> ReadDataAsync(IRequestExecutor executor, string query)
    {
        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
        await using (result)
        {
            return JsonNode.Parse(result.ExpectOperationResult().ToJson())!["data"]!;
        }
    }

    private static async Task<DeferredResponse> ReadAsync(IRequestExecutor executor, string query)
    {
        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        JsonNode? initialData = null;
        var pending = new List<JsonObject>();
        var incrementalEntries = new List<JsonObject>();

        await using (stream)
        {
            await foreach (var payload in stream.ReadResultsAsync()
                .WithCancellation(TestContext.Current.CancellationToken))
            {
                await using var cleanup = payload;
                var node = JsonNode.Parse(payload.ToJson())!;

                if (node["data"] is { } data)
                {
                    initialData = data.DeepClone();
                }

                if (node["pending"] is JsonArray pendingEntries)
                {
                    foreach (var entry in pendingEntries)
                    {
                        pending.Add(entry!.DeepClone().AsObject());
                    }
                }

                if (node["incremental"] is JsonArray incremental)
                {
                    foreach (var entry in incremental)
                    {
                        incrementalEntries.Add(entry!.DeepClone().AsObject());
                    }
                }
            }
        }

        return new DeferredResponse(initialData!, pending, incrementalEntries);
    }

    // returns the initial data followed by each pending entry, in announcement order,
    // with the data delivered for its id merged in.
    private static List<string> ToEntries(DeferredResponse response)
    {
        var dataById = new Dictionary<string, JsonObject>();

        foreach (var entry in response.Incremental)
        {
            var id = entry["id"]!.GetValue<string>();

            if (!dataById.TryGetValue(id, out var target))
            {
                target = [];
                dataById.Add(id, target);
            }

            if (entry["data"] is JsonObject entryData)
            {
                MergeObject(target, entryData);
            }
        }

        var entries = new List<string> { response.InitialData.ToJsonString(s_indented) };

        foreach (var pendingEntry in response.Pending)
        {
            var entry = pendingEntry.DeepClone().AsObject();
            var id = entry["id"]!.GetValue<string>();
            entry["data"] = dataById.TryGetValue(id, out var data) ? data.DeepClone() : new JsonObject();
            entries.Add(entry.ToJsonString(s_indented));
        }

        return entries;
    }

    // applies each incremental data object to the initial data at the path of its pending entry
    // plus the sub path of the incremental entry.
    private static JsonNode Merge(DeferredResponse response)
    {
        var merged = response.InitialData.DeepClone();

        foreach (var entry in response.Incremental)
        {
            var id = entry["id"]!.GetValue<string>();
            var pending = response.Pending.Single(p => p["id"]!.GetValue<string>() == id);
            var target = merged;
            var segments = pending["path"]!.AsArray().AsEnumerable();

            if (entry["subPath"] is JsonArray subPath)
            {
                segments = segments.Concat(subPath);
            }

            foreach (var segment in segments)
            {
                target = segment!.GetValueKind() is JsonValueKind.Number
                    ? target![segment.GetValue<int>()]
                    : target![segment.GetValue<string>()];
            }

            if (entry["data"] is JsonObject entryData)
            {
                MergeObject(target!.AsObject(), entryData);
            }
        }

        return merged;
    }

    private sealed record DeferredResponse(
        JsonNode InitialData,
        List<JsonObject> Pending,
        List<JsonObject> Incremental);

    private static void MergeObject(JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source)
        {
            if (value is JsonObject sourceObject && target[name] is JsonObject targetObject)
            {
                MergeObject(targetObject, sourceObject);
            }
            else
            {
                target[name] = value!.DeepClone();
            }
        }
    }
}
