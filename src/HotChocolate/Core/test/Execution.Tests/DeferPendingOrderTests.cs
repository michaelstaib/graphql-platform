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

    // returns the initial data followed by each pending entry, in announcement order,
    // with the data delivered for its id merged in.
    private static async Task<List<string>> ExecuteAsync(string query)
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDocumentFromString(
                """
                type Query { a: A x: String y: String }
                type A { x: String y: String }
                """)
            .AddResolver("Query", "a", _ => new object())
            .AddResolver("Query", "x", _ => "qx")
            .AddResolver("Query", "y", _ => "qy")
            .AddResolver("A", "x", _ => "x")
            .AddResolver("A", "y", _ => "y")
            .ModifyOptions(o => o.EnableDefer = true)
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        JsonNode? initialData = null;
        var pending = new List<JsonObject>();
        var dataById = new Dictionary<string, JsonObject>();

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
                        var id = entry!["id"]!.GetValue<string>();

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
                }
            }
        }

        var entries = new List<string> { initialData!.ToJsonString(s_indented) };

        foreach (var entry in pending)
        {
            var id = entry["id"]!.GetValue<string>();
            entry["data"] = dataById.TryGetValue(id, out var data) ? data.DeepClone() : new JsonObject();
            entries.Add(entry.ToJsonString(s_indented));
        }

        return entries;
    }

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
