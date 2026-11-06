using System.Buffers;
using System.Text;
using System.Text.Json;
using CookieCrumble.HotChocolate;
using CookieCrumble.HotChocolate.Formatters;
using CookieCrumble.Xunit3;
using HotChocolate.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Text.Json;
using Path = HotChocolate.Path;

namespace CookieCrumble;

public class ExecutionResultSnapshotTests
{
    static ExecutionResultSnapshotTests()
    {
        Snapshot.RegisterTestFramework(new Xunit3Framework());
    }

    [Fact]
    public void Format_Should_WriteEntriesInEmissionOrder_When_IdHasSeveralIncrementalEntries()
    {
        // arrange
        var stream = CreateStream(
            new Dictionary<string, object?>(),
            new PendingResult(1, Path.Root, "items"),
            CreateEntry(1, "b", "B"),
            CreateEntry(1, "a", "A"),
            CreateEntry(1, "c", "C"));

        // act
        var result = Format(stream);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {},
              "pending": [
                {
                  "id": "1",
                  "path": [],
                  "label": "items"
                }
              ],
              "incremental": [
                {
                  "id": "1",
                  "subPath": [
                    "b"
                  ],
                  "data": {
                    "name": "B"
                  }
                },
                {
                  "id": "1",
                  "subPath": [
                    "a"
                  ],
                  "data": {
                    "name": "A"
                  }
                },
                {
                  "id": "1",
                  "subPath": [
                    "c"
                  ],
                  "data": {
                    "name": "C"
                  }
                }
              ],
              "completed": [
                {
                  "id": "1"
                }
              ],
              "hasNext": false
            }
            """);
    }

    [Fact]
    public void Format_Should_AcceptSubPathEntry_When_ScopeRejectsOverlapsAndPositionDiffersFromFragmentPath()
    {
        // arrange
        var stream = CreateStream(
            new Dictionary<string, object?>
            {
                ["node"] = new Dictionary<string, object?>
                {
                    ["child"] = new Dictionary<string, object?> { ["id"] = "1" }
                }
            },
            new PendingResult(0, Path.Root.Append("node")),
            CreateEntry(0, "child", "a"));
        using var scope = IncrementalDeliveryChecks.RejectOverlappingPositions();

        // act
        var result = Format(stream);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "node": {
                  "child": {
                    "id": "1"
                  }
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "node"
                  ]
                }
              ],
              "incremental": [
                {
                  "id": "0",
                  "subPath": [
                    "child"
                  ],
                  "data": {
                    "name": "a"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """);
    }

    private static string Format(ResponseStream stream)
    {
        var buffer = new ArrayBufferWriter<byte>();
        SnapshotValueFormatters.ExecutionResult.Format(buffer, stream);
        return Encoding.UTF8.GetString(buffer.WrittenSpan).TrimEnd();
    }

    private static ResponseStream CreateStream(
        Dictionary<string, object?> initialData,
        PendingResult pending,
        params IncrementalObjectResult[] entries)
    {
        var initial = new OperationResult(CreateData(initialData))
        {
            Pending = [pending],
            HasNext = true
        };

        var incremental = new OperationResult(
            ImmutableOrderedDictionary<string, object?>.Empty.Add("__placeholder", true))
        {
            Incremental = [.. entries],
            Completed = [new CompletedResult(pending.Id)],
            HasNext = false,
            Extensions = []
        };

        return new ResponseStream(() => ReadAsync(initial, incremental), ExecutionResultKind.DeferredResult);
    }

    private static async IAsyncEnumerable<OperationResult> ReadAsync(params OperationResult[] results)
    {
        foreach (var result in results)
        {
            yield return result;
            await Task.Yield();
        }
    }

    private static IncrementalObjectResult CreateEntry(int id, string subPath, string name)
        => new(
            id,
            subPath: Path.Root.Append(subPath),
            data: CreateData(new Dictionary<string, object?> { ["name"] = name }));

    private static OperationResultData CreateData(object value)
        => new(value, isValueNull: false, new ValueFormatter(value), memoryHolder: null);

    private sealed class ValueFormatter(object value) : IRawJsonFormatter
    {
        public void WriteDataTo(JsonWriter jsonWriter)
            => JsonValueFormatter.WriteValue(jsonWriter, value, new JsonSerializerOptions());
    }
}
