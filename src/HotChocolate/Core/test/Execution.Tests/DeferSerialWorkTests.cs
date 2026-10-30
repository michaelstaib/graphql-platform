using System.Collections.Concurrent;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class DeferSerialWorkTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Defer_Should_Complete_When_DeferredBranchContainsSerialField()
    {
        // arrange
        var executor = await CreateExecutorAsync(new ConcurrentQueue<string>());

        // act
        var payloads = await ReadPayloadsAsync(executor, "{ root ... @defer { serial } }");

        // assert
        Assert.Contains(payloads, payload => payload.Contains("\"serial\": \"serial\"", StringComparison.Ordinal));
        Assert.Contains("\"hasNext\": false", payloads[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Defer_Should_RunSerialFieldsInOrderAfterParallelFields_When_DeferredBranchMixesBoth()
    {
        // arrange
        var order = new ConcurrentQueue<string>();
        var executor = await CreateExecutorAsync(order);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ ... @defer { firstSerial parallel secondSerial } }");

        // assert
        Assert.Equal(["parallel", "firstSerial", "secondSerial"], order.ToArray());
        Assert.Contains("\"hasNext\": false", payloads[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Defer_Should_Complete_When_NestedDeferredBranchContainsSerialField()
    {
        // arrange
        var executor = await CreateExecutorAsync(new ConcurrentQueue<string>());

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ root ... @defer { parallel ... @defer { serial } } }");

        // assert
        Assert.Contains(payloads, payload => payload.Contains("\"serial\": \"serial\"", StringComparison.Ordinal));
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

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(ConcurrentQueue<string> order)
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor =>
                {
                    descriptor.Field("root").Type<StringType>().Resolve("root");
                    descriptor.Field("serial").Type<StringType>().Serial().Resolve("serial");
                    descriptor
                        .Field("firstSerial")
                        .Type<StringType>()
                        .Serial()
                        .Resolve(_ =>
                        {
                            order.Enqueue("firstSerial");
                            return "firstSerial";
                        });
                    descriptor
                        .Field("secondSerial")
                        .Type<StringType>()
                        .Serial()
                        .Resolve(_ =>
                        {
                            order.Enqueue("secondSerial");
                            return "secondSerial";
                        });
                    descriptor
                        .Field("parallel")
                        .Type<StringType>()
                        .Resolve(async _ =>
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                            order.Enqueue("parallel");
                            return "parallel";
                        });
                })
            .ModifyOptions(options => options.EnableDefer = true)
            .BuildRequestExecutorAsync();
}
