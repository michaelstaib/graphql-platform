using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class DeferBelowNulledParentTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Execute_Should_NotAnnounceOrResolveGroup_When_ItsParentIsNulledBeforeTheGroupRuns()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ a { holder { nnBoom } ... @defer { holder { nnBoom ok } } } }");

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "a",
                        "holder",
                        "nnBoom"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ],
                  "data": null,
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal(0, probe.OkCount);
    }

    [Fact]
    public async Task Execute_Should_NotAnnounceGroup_When_ItsParentIsNulledAfterTheGroupResolved()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ a { slowNull ... @defer { x } } }",
            async () =>
            {
                await probe.XResolved.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
                probe.SlowNull.TrySetResult();
            });

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "a",
                        "slowNull"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ],
                  "data": null,
                  "hasNext": false
                }
                """
            ]);
    }

    [Fact]
    public async Task Execute_Should_NotAnnounceGroup_When_ANullableAncestorFieldIsNulled()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ a { x maybeHolder { nnBoom inner { ... @defer { ok } } } } }");

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "a",
                        "maybeHolder",
                        "nnBoom"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ],
                  "data": {
                    "a": {
                      "x": "x",
                      "maybeHolder": null
                    }
                  },
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal(0, probe.OkCount);
    }

    private static Task<List<string>> ReadPayloadsAsync(IRequestExecutor executor, string query)
        => ReadPayloadsAsync(executor, query, () => Task.CompletedTask);

    private static async Task<List<string>> ReadPayloadsAsync(
        IRequestExecutor executor,
        string query,
        Func<Task> whileExecuting)
    {
        return await Task.Run(
                async () =>
                {
                    var payloads = new List<string>();
                    var execution = executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    await whileExecuting();
                    await using var result = await execution;
                    var stream = Assert.IsType<ResponseStream>(result);

                    await foreach (var payload in stream
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

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(Probe probe)
        => new ServiceCollection()
            .AddSingleton(probe)
            .AddGraphQL()
            .AddQueryType<Query>()
            .ModifyOptions(options => options.EnableDefer = true)
            .BuildRequestExecutorAsync();

    public sealed class Query
    {
        public A A => new();
    }

    public sealed class A
    {
        public string GetX([Service] Probe probe)
        {
            probe.XResolved.TrySetResult();
            return "x";
        }

        [GraphQLNonNullType]
        public async Task<string?> GetSlowNullAsync([Service] Probe probe)
        {
            await probe.SlowNull.Task.ConfigureAwait(false);
            return null;
        }

        public Holder Holder => new();

        public Holder? MaybeHolder => new();
    }

    public sealed class Holder
    {
        public string GetOk([Service] Probe probe)
        {
            probe.Ok();
            return "ok";
        }

        [GraphQLNonNullType]
        public string? NnBoom => null;

        public Inner Inner => new();
    }

    public sealed class Inner
    {
        public string GetOk([Service] Probe probe)
        {
            probe.Ok();
            return "ok";
        }
    }

    public sealed class Probe
    {
        private int _okCount;

        public int OkCount => Volatile.Read(ref _okCount);

        public TaskCompletionSource XResolved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SlowNull { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Ok() => Interlocked.Increment(ref _okCount);
    }
}
