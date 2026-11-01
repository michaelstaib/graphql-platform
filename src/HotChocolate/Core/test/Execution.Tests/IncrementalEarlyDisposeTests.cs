using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class IncrementalEarlyDisposeTests
{
    private const string ProcessingFailure = "processing failed";
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
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            await ReadPayloadAsync(enumerator, payloads);
            await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            probe.Gate("1").TrySetResult();
            await ReadPayloadAsync(enumerator, payloads);
        }

        await stream.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Cancelled("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        payloads.MatchInlineSnapshots(
        [
            "data={\"items\":[{\"id\":\"1\"},{\"id\":\"2\"}]}; pending=2; hasNext=True",
            "incremental={\"slow\":\"slow 1\"}; completed=1; hasNext=True"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_AbandonRequestContexts_When_DeferredResolverIgnoresCancellation()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var listener = new ArenaListener(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { stubborn } } }",
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

        await stream.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var poolStateAfterDispose = probe.PoolState;

        probe.Gate("1").TrySetResult();
        await probe.Returned("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        probe.Gate("2").TrySetResult();
        await probe.Returned("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        poolStateAfterDispose.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        probe.Events.ToArray().MatchInlineSnapshots(
        [
            "ArenaAbandoned",
            "resolver 1 returned",
            "resolver 2 returned"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ReturnRequestContexts_When_StreamIsReadToCompletion()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var listener = new ArenaListener(probe);
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

        // assert
        Assert.Equal(3, payloads.Count);
        Assert.Empty(probe.Errors);
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 1; operation contexts returned: 1");
        probe.Events.ToArray().MatchInlineSnapshots(["ArenaSealed"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotReturnRequestContext_When_AbortedWhileExecutionRuns()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        using var abort = new CancellationTokenSource();
        var aborted = executor.ExecuteAsync("{ items { id stubborn ... @defer { quick } } }", abort.Token);
        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // act
        await abort.CancelAsync();
        await using var abortedResult = await aborted.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var poolStateWhileRunning = probe.PoolState;

        probe.Gate("1").TrySetResult();
        await probe.Returned("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        probe.Gate("2").TrySetResult();
        await probe.Returned("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        poolStateWhileRunning.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnRequestContext_When_ExecutionCompletes()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);

        // act
        var result = await executor.ExecuteAsync("{ items { id } }", TestContext.Current.CancellationToken);
        await result.DisposeAsync();

        // assert
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 1; operation contexts returned: 1");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnRequestContext_When_DocumentHasSyntaxError()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);

        // act
        var result = await executor.ExecuteAsync("{ items { id ", TestContext.Current.CancellationToken);
        await result.DisposeAsync();

        // assert
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 1; operation contexts returned: 0");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnRequestContext_When_DocumentFailsValidation()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);

        // act
        var result = await executor.ExecuteAsync("{ nope }", TestContext.Current.CancellationToken);
        await result.DisposeAsync();

        // assert
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 1; operation contexts returned: 0");
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

        // assert
        Assert.Equal(1, probe.ProducerDisposeCount);
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        payloads.MatchInlineSnapshots(
        [
            "data={\"numbers\":[]}; pending=1; incremental=[1]; hasNext=True"
        ]);
    }

    [Fact]
    public async Task DisposeAsync_Should_AbandonRequestContexts_When_StreamSourceIgnoresCancellation()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ stubbornNumbers @stream(initialCount: 0) }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            await ReadPayloadAsync(enumerator, []);
            await probe.Started("stubborn").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        await stream.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var eventsAfterDispose = probe.Events.ToArray();
        var poolStateAfterDispose = probe.PoolState;

        probe.Gate("stubborn").TrySetResult();
        await probe.EnumeratorDisposed.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Empty(probe.Errors);
        Assert.Empty(eventsAfterDispose);
        poolStateAfterDispose.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        probe.PoolState.MatchInlineSnapshot("request contexts returned: 0; operation contexts returned: 0");
        probe.Events.ToArray().MatchInlineSnapshots(["source returned", "enumerator disposed"]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ObserveExecutionFault_When_ExecutionFaultedBeforeDisposal()
    {
        // arrange
        // a process wide timer captures the execution context of the first request,
        // so a warm-up request keeps the observed request collectable.
        await ExecuteDeferredToCompletionAsync();
        var unobserved = new ConcurrentQueue<string>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception.InnerException is { Message: ProcessingFailure } exception)
            {
                unobserved.Enqueue(exception.Message);
            }
        };
        TaskScheduler.UnobservedTaskException += handler;

        try
        {
            // act
            await DisposeAfterExecutionFaultedAsync();

            // the abandoned request becomes unreachable shortly after its last resolver has returned.
            for (var i = 0; i < 10 && unobserved.IsEmpty; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            // assert
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ExecuteDeferredToCompletionAsync()
    {
        var executor = await CreateFaultingExecutorAsync(new Probe());
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { quick } } }",
            CancellationToken.None);
        var stream = Assert.IsType<ResponseStream>(result);

        await using (stream)
        {
            await foreach (var payload in stream.ReadResultsAsync())
            {
                await payload.DisposeAsync();
            }
        }
    }

    // The stream and the execution task must be unreachable once this method returns,
    // so that the garbage collector can finalize an unobserved faulted execution.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task DisposeAfterExecutionFaultedAsync()
    {
        var probe = new Probe();
        var executor = await CreateFaultingExecutorAsync(probe);
        var result = await executor.ExecuteAsync(
            "{ items { id ... @defer { slow } } }",
            CancellationToken.None);
        var stream = Assert.IsType<ResponseStream>(result);

        await using (var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(CancellationToken.None))
        {
            await ReadPayloadAsync(enumerator, []);
            await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

            // the scheduler fails when it resumes after resolver 1 has completed.
            probe.FailProcessing();
            probe.Gate("1").TrySetResult();
            await probe.ProcessingFailed.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            probe.Gate("2").TrySetResult();

            // the fault unwinds on the scheduler thread right after the listener has thrown.
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        await stream.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);
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
        => new ServiceCollection()
            .AddSingleton(probe)
            .AddSingleton<ObjectPool<OperationContext>>(
                sp => new TrackingOperationContextPool(sp.GetRequiredService<IFactory<OperationContext>>(), probe))
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
                })
            .BuildRequestExecutorAsync();

    private static ValueTask<IRequestExecutor> CreateFaultingExecutorAsync(Probe probe)
        => new ServiceCollection()
            .AddSingleton(probe)
            .AddGraphQL()
            .AddQueryType<Query>()
            .AddDiagnosticEventListener(_ => new ProcessingListener(probe))
            .ModifyOptions(options => options.EnableDefer = true)
            .BuildRequestExecutorAsync();

    public sealed class Query
    {
        public List<Item> GetItems() => [new("1"), new("2")];

        public IAsyncEnumerable<int> GetNumbers([Service] Probe probe)
            => new CountingEnumerable(probe);

        public IAsyncEnumerable<int> GetStubbornNumbers([Service] Probe probe)
            => new StubbornEnumerable(probe);
    }

    public sealed class Item(string id)
    {
        public string Id => id;

        public async Task<string> GetSlowAsync(
            [Service] Probe probe,
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

        public async Task<string> GetStubbornAsync([Service] Probe probe)
        {
            probe.Started(id).TrySetResult();
            await probe.Gate(id).Task;
            probe.Log($"resolver {id} returned");
            probe.Returned(id).TrySetResult();
            return $"stubborn {id}";
        }

        public async Task<string> GetFailingCleanupAsync(
            IResolverContext context,
            [Service] Probe probe)
        {
            ((IMiddlewareContext)context).RegisterForCleanup(
                static () => ValueTask.FromException(new InvalidOperationException("cleanup failed")),
                CleanAfter.Request);
            probe.Started(id).TrySetResult();
            await probe.Gate(id).Task;
            return $"failing {id}";
        }

        public string GetQuick() => $"quick {id}";
    }

    public sealed class Probe
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _sources = new();
        private readonly ConcurrentQueue<string> _events = new();
        private readonly ConcurrentQueue<string> _errors = new();
        private int _producerDisposeCount;
        private int _requestContextsReturned;
        private int _operationContextsReturned;
        private int _failProcessing;

        public IReadOnlyCollection<string> Events => _events;

        public IReadOnlyCollection<string> Errors => _errors;

        public string PoolState
            => $"request contexts returned: {Volatile.Read(ref _requestContextsReturned)}; "
                + $"operation contexts returned: {Volatile.Read(ref _operationContextsReturned)}";

        public TaskCompletionSource EnumeratorDisposed { get; } = Create();

        public TaskCompletionSource ProducerDisposedSignal { get; } = Create();

        public TaskCompletionSource ProcessingFailed { get; } = Create();

        public bool IsProcessingFailing => Volatile.Read(ref _failProcessing) is 1;

        public void FailProcessing() => Volatile.Write(ref _failProcessing, 1);

        public int ProducerDisposeCount => Volatile.Read(ref _producerDisposeCount);

        public TaskCompletionSource Gate(string id) => Get("gate", id);

        public TaskCompletionSource Started(string id) => Get("started", id);

        public TaskCompletionSource Cancelled(string id) => Get("cancelled", id);

        public TaskCompletionSource Returned(string id) => Get("returned", id);

        public void Log(string message) => _events.Enqueue(message);

        public void LogError(string message) => _errors.Enqueue(message);

        public void RequestContextReturned() => Interlocked.Increment(ref _requestContextsReturned);

        public void OperationContextReturned() => Interlocked.Increment(ref _operationContextsReturned);

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

    private sealed class TrackingRequestContextPool(Probe probe) : ObjectPool<PooledRequestContext>
    {
        private readonly DefaultObjectPool<PooledRequestContext> _inner = new(new RequestContextPooledObjectPolicy());

        public override PooledRequestContext Get() => _inner.Get();

        public override void Return(PooledRequestContext obj)
        {
            _inner.Return(obj);
            probe.RequestContextReturned();
        }
    }

    // The first context of a request is the one of the operation, the contexts of
    // deferred and streamed branches are rented later and are returned by their tasks.
    private sealed class TrackingOperationContextPool(IFactory<OperationContext> factory, Probe probe)
        : ObjectPool<OperationContext>
    {
        private OperationContext? _operationContext;

        public override OperationContext Get()
        {
            var context = factory.Create();
            Interlocked.CompareExchange(ref _operationContext, context, null);
            return context;
        }

        public override void Return(OperationContext obj)
        {
            if (ReferenceEquals(obj, Volatile.Read(ref _operationContext)))
            {
                probe.OperationContextReturned();
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
            probe.EnumeratorDisposed.TrySetResult();
            return ValueTask.CompletedTask;
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

    private sealed class ProcessingListener(Probe probe) : ExecutionDiagnosticEventListener
    {
        public override void StartProcessing(RequestContext context)
        {
            if (probe.IsProcessingFailing)
            {
                probe.ProcessingFailed.TrySetResult();
                throw new InvalidOperationException(ProcessingFailure);
            }
        }
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
