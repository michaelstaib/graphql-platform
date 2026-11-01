using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class RootBatchResolverTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan s_quietPeriod = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheOnlyRootFieldIsABatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ batchField }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"batchField":"batched-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ABatchFieldHasASiblingRootField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ plain batchField }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"plain":"plain","batchField":"batched-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheBatchFieldIsNested()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ foo { batchField } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"foo":{"batchField":"batched-1"}}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ARootBatchFieldHasANestedBatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ batchFoo { batchField } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"batchFoo":{"batchField":"batched-1"}}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_AMutationHasOnlyBatchRootFields()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "mutation { first second }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"first":"batched-1","second":"batched-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheOnlyNonDeferredRootFieldIsABatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ batchField ... @defer { plain } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"batchField":"batched-1"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            incremental {"id":"2","data":{"plain":"plain"}}
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
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ADeferredFragmentHasANestedBatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ plain ... @defer { foo { batchField } } }");

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
            incremental {"id":"2","data":{"foo":{"batchField":"batched-1"}}}
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
    public async Task ExecuteAsync_Should_FailWithCancellation_When_TheRequestIsCancelledWhileTheRootBatchResolverRuns()
    {
        // arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("waiting")
                    .Type<StringType>()
                    .ResolveBatch(async contexts =>
                    {
                        started.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, contexts[0].RequestAborted);
                        return new ResolverResult[contexts.Count];
                    });
            })
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // act
        var execution = Task.Run(
            async () =>
            {
                try
                {
                    var result = await executor.ExecuteAsync("{ waiting }", cts.Token);
                    return result.ToJson(withIndentations: false);
                }
                catch (OperationCanceledException)
                {
                    return "cancelled";
                }
            },
            TestContext.Current.CancellationToken);

        await started.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        var outcome = await execution.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        outcome.MatchInlineSnapshot(
            """
            {"errors":[{"message":"The GraphQL request execution was canceled.","extensions":{"code":"HC0049"}}]}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ProduceTheResultAfterTheEntryCleanup_When_TheBatchFieldIsTheLastTask()
    {
        // arrange
        var probe = new CleanupProbe();
        var executor = await CreateGatedExecutorAsync(probe);

        // act
        var execution = Task.Run(
            async () =>
            {
                await using var result = await executor.ExecuteAsync(
                    "{ gated }",
                    TestContext.Current.CancellationToken);
                probe.Add("result");
                return result.ToJson(withIndentations: false);
            },
            TestContext.Current.CancellationToken);

        string json;

        try
        {
            await probe.Started.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await WaitForQuietPeriodAsync(execution);
        }
        finally
        {
            probe.Release();
        }

        json = await execution.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"gated":"gated-1"}}
            """);
        probe.Log.MatchInlineSnapshots(
        [
            """
            cleanup started
            """,
            """
            cleanup finished
            """,
            """
            result
            """,
            """
            request cleanup
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheLastPayloadAfterTheEntryCleanup_When_TheBatchFieldIsDeferred()
    {
        // arrange
        var probe = new CleanupProbe();
        var executor = await CreateGatedExecutorAsync(probe);

        // act
        var execution = Task.Run(
            async () =>
            {
                await using var result = await executor.ExecuteAsync(
                    "{ plain ... @defer { gated } }",
                    TestContext.Current.CancellationToken);

                await foreach (var payload in result
                    .ExpectResponseStream()
                    .ReadResultsAsync()
                    .WithCancellation(TestContext.Current.CancellationToken))
                {
                    await using var payloadCleanup = payload;
                    using var document = JsonDocument.Parse(payload.ToJson(withIndentations: false));

                    if (!document.RootElement.GetProperty("hasNext").GetBoolean())
                    {
                        probe.Add("last payload");
                    }
                }
            },
            TestContext.Current.CancellationToken);

        try
        {
            await probe.Started.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await WaitForQuietPeriodAsync(execution);
        }
        finally
        {
            probe.Release();
        }

        await execution.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Log.MatchInlineSnapshots(
        [
            """
            cleanup started
            """,
            """
            cleanup finished
            """,
            """
            last payload
            """,
            """
            request cleanup
            """
        ]);
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

    private static async Task WaitForQuietPeriodAsync(Task execution)
    {
        // a request that completes while its entry cleanup is still gated finishes within this period.
        await Task.WhenAny(execution, Task.Delay(s_quietPeriod, TestContext.Current.CancellationToken));
    }

    private static ValueTask<IRequestExecutor> CreateGatedExecutorAsync(CleanupProbe probe)
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("plain").Type<StringType>().Resolve("plain");
                d.Field("gated")
                    .Type<StringType>()
                    .ResolveBatch(contexts =>
                    {
                        probe.Register(contexts[0]);

                        var results = new ResolverResult[contexts.Count];

                        for (var i = 0; i < contexts.Count; i++)
                        {
                            results[i] = ResolverResult.Ok($"gated-{contexts.Count}");
                        }

                        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
                    });
            })
            .ModifyOptions(o => o.EnableDefer = true)
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static ValueTask<IRequestExecutor> CreateExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("plain").Type<StringType>().Resolve("plain");
                d.Field("foo").Type<ObjectType<Foo>>().Resolve(new Foo());
                d.Field("batchField").Type<StringType>().ResolveBatch(ResolveBatch);
                d.Field("batchFoo").Type<ObjectType<Foo>>().ResolveBatch(ResolveFooBatch);
            })
            .AddMutationType(d =>
            {
                d.Name("Mutation");
                d.Field("first").Type<StringType>().ResolveBatch(ResolveBatch);
                d.Field("second").Type<StringType>().ResolveBatch(ResolveBatch);
            })
            .AddObjectType<Foo>(d => d.Field("batchField").Type<StringType>().ResolveBatch(ResolveBatch))
            .ModifyOptions(o => o.EnableDefer = true)
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

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveFooBatch(IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok(new Foo());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    public sealed class Foo
    {
        public string Name => "foo";
    }

    public sealed class CleanupProbe
    {
        private readonly object _sync = new();
        private readonly List<string> _log = [];
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public string[] Log
        {
            get
            {
                lock (_sync)
                {
                    return _log.ToArray();
                }
            }
        }

        public void Add(string entry)
        {
            lock (_sync)
            {
                _log.Add(entry);
            }
        }

        public void Release() => _gate.TrySetResult();

        public void Register(IResolverContext context)
        {
            var middlewareContext = (IMiddlewareContext)context;

            middlewareContext.RegisterForCleanup(
                async () =>
                {
                    Add("cleanup started");
                    _started.TrySetResult();
                    await _gate.Task;
                    Add("cleanup finished");
                });

            middlewareContext.RegisterForCleanup(
                () =>
                {
                    Add("request cleanup");
                    return default;
                },
                CleanAfter.Request);
        }
    }
}
