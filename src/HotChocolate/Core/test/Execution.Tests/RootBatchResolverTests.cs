using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class RootBatchResolverTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

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
}
