using System.Text.Json;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class StreamSerialWorkTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    [Fact]
    public async Task Stream_Should_Complete_When_StreamedItemsContainSerialField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var payloads = await ReadPayloadsAsync(executor, "{ items @stream(initialCount: 0) { id serial } }");

        // assert
        var items = new List<JsonElement>();
        var completed = new List<string>();

        foreach (var payload in payloads)
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("incremental", out var incremental))
            {
                foreach (var entry in incremental.EnumerateArray())
                {
                    items.AddRange(entry.GetProperty("items").EnumerateArray().Select(item => item.Clone()));
                }
            }

            if (document.RootElement.TryGetProperty("completed", out var completions))
            {
                completed.AddRange(completions.EnumerateArray().Select(c => c.GetProperty("id").GetString()!));
            }
        }

        JsonSerializer.Serialize(new { items, completed }, s_indented).MatchInlineSnapshot(
            """
            {
              "items": [
                {
                  "id": 1,
                  "serial": "serial"
                },
                {
                  "id": 2,
                  "serial": "serial"
                },
                {
                  "id": 3,
                  "serial": "serial"
                }
              ],
              "completed": [
                "2"
              ]
            }
            """);
        Assert.Contains("\"hasNext\": false", payloads[^1], StringComparison.Ordinal);
    }

    private static async Task<List<string>> ReadPayloadsAsync(IRequestExecutor executor, string query)
    {
        // the stream is read off the test thread so that a scheduler that never finishes fails the
        // test through the timeout instead of blocking the test run.
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
                    }

                    return payloads;
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor => descriptor
                    .Field("items")
                    .Type<ListType<ObjectType<Item>>>()
                    .Resolve(_ => ProduceItemsAsync()))
            .AddObjectType<Item>(
                descriptor =>
                {
                    descriptor.Field(t => t.Id);
                    descriptor.Field("serial").Type<StringType>().Serial().Resolve("serial");
                })
            .ModifyOptions(
                options =>
                {
                    options.EnableDefer = true;
                    options.EnableStream = true;
                })
            .BuildRequestExecutorAsync();

    private static async IAsyncEnumerable<Item> ProduceItemsAsync()
    {
        for (var i = 1; i <= 3; i++)
        {
            await Task.Yield();
            yield return new Item(i);
        }
    }

    public sealed class Item(int id)
    {
        public int Id { get; } = id;
    }
}
