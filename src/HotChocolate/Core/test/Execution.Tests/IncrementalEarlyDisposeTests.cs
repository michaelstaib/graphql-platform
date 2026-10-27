using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text.Json;
using HotChocolate.Execution.Configuration;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class IncrementalEarlyDisposeTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DisposeAsync_Should_CancelDeferredResolver_When_StreamIsDisposedAfterFirstPayload()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { slow } } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var payloads = new List<string>();

        // act
        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await ReadPayloadAsync(enumerator, payloads);
            await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            probe.Gate("1").TrySetResult();
            await ReadPayloadAsync(enumerator, payloads);
        }

        await probe.Cancelled("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
        [
            "data={\"items\":[{\"id\":\"1\"},{\"id\":\"2\"}]}; pending=2; hasNext=True",
            "incremental={\"slow\":\"slow 1\"}; completed=1; hasNext=True"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ReleaseRequestResources_When_DeferredResolverIgnoresCancellation()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var listener = new ArenaListener(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { stubborn } } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var eventsAfterDispose = new List<string>();

        // act
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            await ReadPayloadAsync(enumerator, []);
            await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        await stream.DisposeAsync();
        eventsAfterDispose.AddRange(probe.Events);

        probe.Gate("1").TrySetResult();
        await probe.Returned("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        probe.Gate("2").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Empty(eventsAfterDispose);
        Assert.Empty(probe.Errors);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "resolver 1 returned",
            "resolver 2 returned",
            "ArenaAbandoned",
            "request scope disposed",
            "request context returned"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ReleaseRequestResources_When_StreamIsReadToCompletion()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { slow } } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var payloads = new List<string>();

        // act
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            await ReadPayloadAsync(enumerator, payloads);
            probe.Gate("1").TrySetResult();
            probe.Gate("2").TrySetResult();

            while (await MoveNextAsync(enumerator))
            {
                payloads.Add(Summarize(enumerator.Current));
                await enumerator.Current.DisposeAsync();
            }
        }

        await stream.DisposeAsync();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(3, payloads.Count);
        Assert.Empty(probe.Errors);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "request scope disposed",
            "request context returned"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_CancelAndDisposeProducer_When_StreamIsDisposedAfterFirstBatch()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ numbers @stream(initialCount: 0) }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var payloads = new List<string>();

        // act
        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await ReadPayloadAsync(enumerator, payloads);
            await probe.Started("producer").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        await probe.Cancelled("producer").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.ProducerDisposedSignal.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(1, probe.ProducerDisposeCount);
        payloads.MatchInlineSnapshots(
        [
            "data={\"numbers\":[]}; pending=1; incremental=[1]; hasNext=True"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ReleaseRequestResourcesAfterSource_When_StreamSourceIgnoresCancellation()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ stubbornNumbers @stream(initialCount: 0) }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var eventsAfterDispose = new List<string>();

        // act
        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await ReadPayloadAsync(enumerator, []);
            await probe.Started("stubborn").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        eventsAfterDispose.AddRange(probe.Events);
        probe.Gate("stubborn").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Empty(eventsAfterDispose);
        Assert.Empty(probe.Errors);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "source returned",
            "enumerator disposed",
            "request scope disposed",
            "request context returned"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ThrowCleanupFailureAfterSourceDisposal_When_SourceDisposesAsynchronously()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ failingNumbers @stream(initialCount: 0) { value } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var eventsAtLastPayload = new List<string>();
        var hasNext = true;

        // act
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            while (hasNext)
            {
                Assert.True(await MoveNextAsync(enumerator));
                hasNext = Summarize(enumerator.Current).EndsWith("hasNext=True", StringComparison.Ordinal);
                await enumerator.Current.DisposeAsync();
            }

            await probe.Started("dispose").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            eventsAtLastPayload.AddRange(probe.Events);
            probe.Gate("dispose").TrySetResult();

            while (await MoveNextAsync(enumerator))
            {
                await enumerator.Current.DisposeAsync();
            }
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => stream.DisposeAsync().AsTask());

        // assert
        Assert.Equal("cleanup failed", exception.Message);
        Assert.Empty(probe.Errors);
        eventsAtLastPayload.MatchInlineSnapshots(
        [
            "source returned",
            "enumerator dispose started"
        ]);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "source returned",
            "enumerator dispose started",
            "enumerator disposed",
            "request scope disposed",
            "request context returned"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ReportCleanupFailures_When_TheExecutionSettlesAfterTheStreamWasDisposed()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { failingCleanup } } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            await ReadPayloadAsync(enumerator, []);
            await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        await stream.DisposeAsync();
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Errors.ToArray().MatchInlineSnapshots(
        [
            "request error: cleanup failed",
            "request error: cleanup failed"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ReportCleanupFailure_When_TheStreamProducerReleasesLast()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ stubbornFailingNumbers @stream(initialCount: 0) { value } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await ReadPayloadAsync(enumerator, []);
            await probe.Started("stubborn").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        probe.Gate("stubborn").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "source returned",
            "enumerator disposed",
            "request scope disposed",
            "request context returned"
        ]);
        probe.Errors.ToArray().MatchInlineSnapshots(["request error: cleanup failed"]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ThrowCleanupFailure_When_TheStreamIsDisposedLast()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { failingCleanup } } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            await ReadPayloadAsync(enumerator, []);
            probe.Gate("1").TrySetResult();
            probe.Gate("2").TrySetResult();

            while (await MoveNextAsync(enumerator))
            {
                await enumerator.Current.DisposeAsync();
            }
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => stream.DisposeAsync().AsTask());

        // assert
        Assert.Equal("cleanup failed", exception.Message);
        Assert.Empty(probe.Errors);
    }

    [Fact]
    public async Task ReadResultsAsync_Should_SealArenaAndReturnPages_When_StreamIsReadToCompletion()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var listener = new ArenaListener(probe);

        // act
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { quick } } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        await using (stream)
        {
            await foreach (var payload in stream.ReadResultsAsync()
                .WithCancellation(TestContext.Current.CancellationToken))
            {
                await payload.DisposeAsync();
            }
        }

        // assert
        listener.Events.ToArray().MatchInlineSnapshots(["ArenaSealed"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_KeepRequestContextOutOfThePool_When_AbortedBeforeTheFirstPayload()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var abort = new CancellationTokenSource();
        var aborted = executor.ExecuteAsync("{ items { id stubborn ... @defer { quick } } }", abort.Token);
        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        await using var abortedResult = await aborted;
        var abortedContext = Assert.Single(probe.RentedContexts);

        // act
        var second = await executor.ExecuteAsync("{ items { id } }", TestContext.Current.CancellationToken);
        await second.DisposeAsync();
        var isReturnedWhileRunning = probe.ContextReturned(abortedContext).Task.IsCompleted;
        var secondContext = probe.RentedContexts[1];

        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();
        await probe.ContextReturned(abortedContext).Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.False(isReturnedWhileRunning);
        Assert.NotSame(abortedContext, secondContext);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DisposeRequestScopeAfterResolverFinished_When_AbortedBeforeFirstPayload()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var abort = new CancellationTokenSource();
        var aborted = executor.ExecuteAsync("{ tracked items { id scoped ... @defer { quick } } }", abort.Token);
        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // act
        await abort.CancelAsync();
        await using var abortedResult = await aborted;
        var eventsAfterAbort = probe.Events.ToArray();

        probe.Gate("1").TrySetResult();
        await probe.Returned("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        probe.Gate("2").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Empty(eventsAfterAbort);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "resolver 1 resolved the scoped service",
            "resolver 2 resolved the scoped service",
            "request cleanup ran",
            "request scope disposed",
            "request context returned"
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RunCleanupAfterResolverFinished_When_MiddlewareFailsAfterExecution()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorWithFailingMiddlewareAsync(probe, readResultsFirst: false);

        // act
        await using var result = await executor.ExecuteAsync(
            "{ tracked items { id ... @defer { scoped } } }",
            TestContext.Current.CancellationToken);
        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var eventsBeforeResolvers = probe.Events.ToArray();

        probe.Gate("1").TrySetResult();
        await probe.Returned("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        probe.Gate("2").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Empty(eventsBeforeResolvers);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "resolver 1 resolved the scoped service",
            "resolver 2 resolved the scoped service",
            "request cleanup ran",
            "operation context returned",
            "request scope disposed",
            "request context returned"
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReleaseDroppedResult_When_MiddlewareFailsAfterExecutionCompleted()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorWithFailingMiddlewareAsync(probe, readResultsFirst: true);

        // act
        await using var result = await executor.ExecuteAsync(
            "{ tracked items { id ... @defer { quick } } }",
            TestContext.Current.CancellationToken);
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Errors.ToArray().MatchInlineSnapshots(["request error: request middleware failed"]);
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "request cleanup ran",
            "operation context returned",
            "request context returned",
            "request scope disposed"
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CancelAbandonedExecution_When_MiddlewareFailsAfterExecution()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorWithFailingMiddlewareAsync(probe, readResultsFirst: false);

        // act
        await using var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { slow } } }",
            TestContext.Current.CancellationToken);
        await probe.Cancelled("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Cancelled("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.ScopeDisposed.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Events.Order().ToArray().MatchInlineSnapshots(
        [
            "operation context returned",
            "request context returned",
            "request scope disposed"
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReportCancellationFailure_When_MiddlewareFailsAfterExecution()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorWithFailingMiddlewareAsync(probe, readResultsFirst: false);

        // act
        await using var result = await executor.ExecuteAsync(
            "{ failingCancellation items { id ... @defer { stubborn } } }",
            TestContext.Current.CancellationToken);
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Errors.ToArray().MatchInlineSnapshots(
        [
            "request error: One or more errors occurred. (cancellation callback failed)",
            "request error: request middleware failed"
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReportCleanupFailure_When_MiddlewareFailsAfterExecution()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorWithFailingMiddlewareAsync(probe, readResultsFirst: false);

        // act
        await using var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { failingCleanup } } }",
            TestContext.Current.CancellationToken);
        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();
        await probe.RequestContextReturned.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.Errors.ToArray().MatchInlineSnapshots(
        [
            "request error: request middleware failed",
            "request error: cleanup failed"
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_KeepBatchContextOutOfThePool_When_MiddlewareFailsAfterIncrementalBatch()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorWithFailingMiddlewareAsync(probe, readResultsFirst: false);
        var request = OperationRequestBuilder
            .New()
            .SetDocument("query($defer: Boolean!) { items { id ... @defer(if: $defer) { stubborn } } }")
            .SetVariableValues(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["defer"] = true },
                    new Dictionary<string, object?> { ["defer"] = true }
                })
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);
        var isReturnedWhileRunning = probe.OperationContextReturned.Task.IsCompleted;
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();

        // assert
        Assert.False(isReturnedWhileRunning);
    }

    private static Task<bool> MoveNextAsync(IAsyncEnumerator<OperationResult> enumerator)
        => enumerator.MoveNextAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

    private static async Task ReadPayloadAsync(IAsyncEnumerator<OperationResult> enumerator, List<string> payloads)
    {
        Assert.True(await MoveNextAsync(enumerator));
        payloads.Add(Summarize(enumerator.Current));
        await enumerator.Current.DisposeAsync();
    }

    // The delivery ids come from a pooled branch tracker, so the summary keeps only stable parts.
    private static string Summarize(OperationResult payload)
    {
        using var document = JsonDocument.Parse(payload.ToJson());
        var root = document.RootElement;
        var parts = new List<string>();

        if (root.TryGetProperty("data", out var data))
        {
            parts.Add($"data={JsonSerializer.Serialize(data)}");
        }

        if (root.TryGetProperty("pending", out var pending))
        {
            parts.Add($"pending={pending.GetArrayLength()}");
        }

        if (root.TryGetProperty("incremental", out var incremental))
        {
            foreach (var entry in incremental.EnumerateArray())
            {
                var content = entry.TryGetProperty("data", out var entryData) ? entryData : entry.GetProperty("items");
                parts.Add($"incremental={JsonSerializer.Serialize(content)}");
            }
        }

        if (root.TryGetProperty("completed", out var completed))
        {
            parts.Add($"completed={completed.GetArrayLength()}");
        }

        parts.Add($"hasNext={root.GetProperty("hasNext").GetBoolean()}");
        return string.Join("; ", parts);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(Probe probe)
        => CreateExecutorBuilder(probe).BuildRequestExecutorAsync();

    private static ValueTask<IRequestExecutor> CreateExecutorWithFailingMiddlewareAsync(
        Probe probe,
        bool readResultsFirst)
    {
        var builder = CreateExecutorBuilder(probe);
        builder.Services.AddSingleton<IFactory<OperationContextOwner>>(
            sp => new OperationContextOwnerFactory(
                new TrackingOperationContextPool(probe, sp.GetRequiredService<ObjectPool<OperationContext>>())));

        return builder
            .UseRequest(
                (_, next) => async context =>
                {
                    await next(context);

                    if (readResultsFirst && context.Result is ResponseStream stream)
                    {
                        await foreach (var payload in stream.ReadResultsAsync())
                        {
                            await payload.DisposeAsync();
                        }
                    }

                    throw new InvalidOperationException("request middleware failed");
                },
                key: "FailAfterExecution",
                after: WellKnownRequestMiddleware.OperationExecutionMiddleware)
            .BuildRequestExecutorAsync();
    }

    private static IRequestExecutorBuilder CreateExecutorBuilder(Probe probe)
        => new ServiceCollection()
            .AddSingleton(probe)
            .AddScoped<ScopeProbe>()
            .AddGraphQL()
            .AddQueryType<Query>()
            .AddDiagnosticEventListener(_ => new ErrorListener(probe))
            .ConfigureSchemaServices(
                services => services.AddSingleton<ObjectPool<PooledRequestContext>>(
                    new TrackingRequestContextPool(probe)))
            .ModifyOptions(
                options =>
                {
                    options.EnableDefer = true;
                    options.EnableStream = true;
                    options.DefaultQueryDependencyInjectionScope = DependencyInjectionScope.Request;
                });

    public sealed class Query
    {
        public List<Item> GetItems() => [new("1"), new("2")];

        public string GetTracked(IResolverContext context, [Service] Probe probe)
        {
            ((IMiddlewareContext)context).RegisterForCleanup(
                () =>
                {
                    probe.Log("request cleanup ran");
                    return ValueTask.CompletedTask;
                },
                CleanAfter.Request);
            return "tracked";
        }

        public string GetFailingCancellation(CancellationToken cancellationToken)
        {
            cancellationToken.Register(
                static () => throw new InvalidOperationException("cancellation callback failed"));
            return "registered";
        }

        public IAsyncEnumerable<int> GetNumbers([Service] Probe probe, [Service] ScopeProbe scopeProbe)
            => new CountingEnumerable(probe);

        public IAsyncEnumerable<int> GetStubbornNumbers([Service] Probe probe, [Service] ScopeProbe scopeProbe)
            => new StubbornEnumerable(probe);

        public IAsyncEnumerable<Number> GetStubbornFailingNumbers(
            IResolverContext context,
            [Service] Probe probe,
            [Service] ScopeProbe scopeProbe)
        {
            ((IMiddlewareContext)context).RegisterForCleanup(
                static () => ValueTask.FromException(new InvalidOperationException("cleanup failed")),
                CleanAfter.Request);
            return new StubbornNumberEnumerable(probe);
        }

        public IAsyncEnumerable<Number> GetFailingNumbers(
            IResolverContext context,
            [Service] Probe probe,
            [Service] ScopeProbe scopeProbe)
        {
            ((IMiddlewareContext)context).RegisterForCleanup(
                static () => ValueTask.FromException(new InvalidOperationException("cleanup failed")),
                CleanAfter.Request);
            return new SlowDisposeEnumerable(probe);
        }
    }

    public sealed class Number(int value)
    {
        public int Value => value;
    }

    public sealed class Item(string id)
    {
        public string Id => id;

        public async Task<string> GetSlowAsync(
            [Service] Probe probe,
            [Service] ScopeProbe scopeProbe,
            CancellationToken cancellationToken)
        {
            probe.Started(id).TrySetResult();

            try
            {
                await probe.Gate(id).Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                probe.Cancelled(id).TrySetResult();
                throw;
            }

            return $"slow {id}";
        }

        public async Task<string> GetStubbornAsync([Service] Probe probe, [Service] ScopeProbe scopeProbe)
        {
            probe.Started(id).TrySetResult();
            await probe.Gate(id).Task;
            probe.Log($"resolver {id} returned");
            probe.Returned(id).TrySetResult();
            return $"stubborn {id}";
        }

        public async Task<string> GetFailingCleanupAsync(
            IResolverContext context,
            [Service] Probe probe,
            [Service] ScopeProbe scopeProbe)
        {
            ((IMiddlewareContext)context).RegisterForCleanup(
                static () => ValueTask.FromException(new InvalidOperationException("cleanup failed")),
                CleanAfter.Request);
            probe.Started(id).TrySetResult();
            await probe.Gate(id).Task;
            return $"failing {id}";
        }

        public async Task<string> GetScopedAsync(
            IResolverContext context,
            [Service] Probe probe,
            [Service] ScopeProbe scopeProbe)
        {
            probe.Started(id).TrySetResult();
            await probe.Gate(id).Task;

            try
            {
                _ = context.Services.GetRequiredService<ScopeProbe>();
                probe.Log($"resolver {id} resolved the scoped service");
            }
            catch (ObjectDisposedException)
            {
                probe.Log($"resolver {id} found the request scope disposed");
            }

            probe.Returned(id).TrySetResult();
            return $"scoped {id}";
        }

        public string GetQuick([Service] ScopeProbe scopeProbe) => $"quick {id}";
    }

    public sealed class Probe
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _sources = new();
        private readonly ConcurrentQueue<string> _events = new();
        private readonly ConcurrentQueue<string> _errors = new();
        private readonly ConcurrentDictionary<RequestContext, TaskCompletionSource> _returnedContexts = new();
        private int _producerDisposeCount;

        public IReadOnlyCollection<string> Events => _events;

        public IReadOnlyCollection<string> Errors => _errors;

        public List<RequestContext> RentedContexts { get; } = [];

        public TaskCompletionSource ContextReturned(RequestContext context)
            => _returnedContexts.GetOrAdd(context, static _ => Create());

        public TaskCompletionSource RequestContextReturned { get; } = Create();

        public TaskCompletionSource ScopeDisposed { get; } = Create();

        public TaskCompletionSource OperationContextReturned { get; } = Create();

        public TaskCompletionSource ProducerDisposedSignal { get; } = Create();

        public int ProducerDisposeCount => Volatile.Read(ref _producerDisposeCount);

        public TaskCompletionSource Gate(string id) => Get("gate", id);

        public TaskCompletionSource Started(string id) => Get("started", id);

        public TaskCompletionSource Cancelled(string id) => Get("cancelled", id);

        public TaskCompletionSource Returned(string id) => Get("returned", id);

        public void Log(string message) => _events.Enqueue(message);

        public void LogError(string message) => _errors.Enqueue(message);

        public void ProducerDisposed()
        {
            Interlocked.Increment(ref _producerDisposeCount);
            Log("producer disposed");
            ProducerDisposedSignal.TrySetResult();
        }

        private TaskCompletionSource Get(string kind, string id)
            => _sources.GetOrAdd($"{kind}:{id}", static _ => Create());

        private static TaskCompletionSource Create()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class ScopeProbe(Probe probe) : IDisposable
    {
        public void Dispose()
        {
            probe.Log("request scope disposed");
            probe.ScopeDisposed.TrySetResult();
        }
    }

    private sealed class TrackingRequestContextPool(Probe probe) : ObjectPool<PooledRequestContext>
    {
        private readonly DefaultObjectPool<PooledRequestContext> _inner = new(new RequestContextPooledObjectPolicy());

        public override PooledRequestContext Get()
        {
            var context = _inner.Get();

            lock (probe.RentedContexts)
            {
                probe.RentedContexts.Add(context);
            }

            return context;
        }

        public override void Return(PooledRequestContext obj)
        {
            _inner.Return(obj);
            probe.Log("request context returned");
            probe.RequestContextReturned.TrySetResult();
            probe.ContextReturned(obj).TrySetResult();
        }
    }

    private sealed class TrackingOperationContextPool(Probe probe, ObjectPool<OperationContext> inner)
        : ObjectPool<OperationContext>
    {
        private OperationContext? _root;

        public override OperationContext Get()
        {
            var context = inner.Get();
            Interlocked.CompareExchange(ref _root, context, null);
            return context;
        }

        public override void Return(OperationContext obj)
        {
            inner.Return(obj);

            if (ReferenceEquals(obj, Volatile.Read(ref _root)))
            {
                probe.Log("operation context returned");
                probe.OperationContextReturned.TrySetResult();
            }
        }
    }

    private sealed class CountingEnumerable(Probe probe) : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new CountingEnumerator(probe, cancellationToken);
    }

    private sealed class CountingEnumerator(Probe probe, CancellationToken cancellationToken) : IAsyncEnumerator<int>
    {
        private int _count;

        public int Current { get; private set; }

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_count is 0)
            {
                _count++;
                Current = 1;
                return true;
            }

            probe.Started("producer").TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                probe.Cancelled("producer").TrySetResult();
                throw;
            }

            return false;
        }

        public ValueTask DisposeAsync()
        {
            probe.ProducerDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubbornEnumerable(Probe probe) : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new StubbornEnumerator(probe);
    }

    private sealed class StubbornEnumerator(Probe probe) : IAsyncEnumerator<int>
    {
        private int _count;

        public int Current { get; private set; }

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_count is 0)
            {
                _count++;
                Current = 1;
                return true;
            }

            probe.Started("stubborn").TrySetResult();
            await probe.Gate("stubborn").Task;
            probe.Log("source returned");
            return false;
        }

        public ValueTask DisposeAsync()
        {
            probe.Log("enumerator disposed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubbornNumberEnumerable(Probe probe) : IAsyncEnumerable<Number>
    {
        public IAsyncEnumerator<Number> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new StubbornNumberEnumerator(probe);
    }

    private sealed class StubbornNumberEnumerator(Probe probe) : IAsyncEnumerator<Number>
    {
        private int _count;

        public Number Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_count is 0)
            {
                _count++;
                Current = new Number(1);
                return true;
            }

            probe.Started("stubborn").TrySetResult();
            await probe.Gate("stubborn").Task;
            probe.Log("source returned");
            return false;
        }

        public ValueTask DisposeAsync()
        {
            probe.Log("enumerator disposed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SlowDisposeEnumerable(Probe probe) : IAsyncEnumerable<Number>
    {
        public IAsyncEnumerator<Number> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new SlowDisposeEnumerator(probe);
    }

    private sealed class SlowDisposeEnumerator(Probe probe) : IAsyncEnumerator<Number>
    {
        private int _count;

        public Number Current { get; private set; } = null!;

        public ValueTask<bool> MoveNextAsync()
        {
            if (_count < 2)
            {
                _count++;
                Current = new Number(_count);
                return new ValueTask<bool>(true);
            }

            probe.Log("source returned");
            return new ValueTask<bool>(false);
        }

        public async ValueTask DisposeAsync()
        {
            probe.Log("enumerator dispose started");
            probe.Started("dispose").TrySetResult();
            await probe.Gate("dispose").Task;
            probe.Log("enumerator disposed");
        }
    }

    private sealed class ErrorListener(Probe probe) : ExecutionDiagnosticEventListener
    {
        public override void ResolverError(IMiddlewareContext context, IError error)
            => probe.LogError($"resolver error: {error.Message}");

        public override void TaskError(IExecutionTask task, IError error)
            => probe.LogError($"task error: {error.Message}");

        public override void RequestError(RequestContext context, Exception error)
            => probe.LogError($"request error: {error.Message}");

        public override void RequestError(RequestContext context, IError error)
            => probe.LogError($"request error: {error.Message}");
    }

    private sealed class ArenaListener : EventListener
    {
        private static readonly AsyncLocal<bool> s_current = new();
        private readonly Probe _probe;

        public ArenaListener(Probe probe)
        {
            _probe = probe;
            s_current.Value = true;
        }

        public ConcurrentQueue<string> Events { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name is "HotChocolate-Buffers-MemoryArena")
            {
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (s_current.Value && eventData.EventName is "ArenaSealed" or "ArenaAbandoned")
            {
                Events.Enqueue(eventData.EventName);
                _probe.Log(eventData.EventName);
            }
        }
    }
}
