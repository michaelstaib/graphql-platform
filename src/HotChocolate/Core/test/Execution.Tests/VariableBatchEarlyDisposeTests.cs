using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class VariableBatchEarlyDisposeTests
{
    private const string ProcessingFailure = "batch processing failed";
    private const int ItemCount = 2;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DisposeAsync_Should_AbandonItemContexts_When_StreamsAreDisposedWhileDeferredResolversRun()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await ExecuteBatchAsync(executor);
        var streams = GetStreams(result);

        // act
        foreach (var stream in streams)
        {
            await ReadInitialPayloadAsync(stream);
        }

        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await result.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var poolStateAfterDispose = probe.PoolState;

        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();
        await probe.Returned("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Returned("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        poolStateAfterDispose.MatchInlineSnapshot("item contexts returned: 0");
        probe.PoolState.MatchInlineSnapshot("item contexts returned: 0");
    }

    [Fact]
    public async Task DisposeAsync_Should_ReturnItemContexts_When_StreamsAreReadToCompletion()
    {
        // arrange
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await ExecuteBatchAsync(executor);
        var streams = GetStreams(result);
        var payloadCounts = new List<int>();

        // act
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();

        foreach (var stream in streams)
        {
            payloadCounts.Add(await ReadToCompletionAsync(stream));
        }

        await result.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal([2, 2], payloadCounts);
        probe.PoolState.MatchInlineSnapshot("item contexts returned: 2");
    }

    [Fact]
    public async Task DisposeAsync_Should_ObserveBatchExecutionFault_When_StreamsAreDisposedBeforeExecutionFinished()
    {
        // arrange
        // a process wide timer captures the execution context of the first request,
        // so a warm-up request keeps the observed request collectable.
        await ExecuteBatchToCompletionAsync();
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
            await DisposeBeforeExecutionFaultsAsync();

            // the abandoned request becomes unreachable shortly after its execution has faulted.
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
    private static async Task ExecuteBatchToCompletionAsync()
    {
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await ExecuteBatchAsync(executor);
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();

        foreach (var stream in GetStreams(result))
        {
            await ReadToCompletionAsync(stream);
        }

        await result.DisposeAsync();
    }

    // The streams and the execution task must be unreachable once this method returns,
    // so that the garbage collector can finalize an unobserved faulted execution.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task DisposeBeforeExecutionFaultsAsync()
    {
        var probe = new Probe();
        var executor = await CreateExecutorAsync(probe);
        var result = await ExecuteBatchAsync(executor);

        foreach (var stream in GetStreams(result))
        {
            await ReadInitialPayloadAsync(stream);
        }

        await probe.Started("1").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await probe.Started("2").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await result.DisposeAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // the scheduler fails when it resumes after the deferred resolvers have returned.
        probe.FailProcessing();
        probe.Gate("1").TrySetResult();
        probe.Gate("2").TrySetResult();
        await probe.ProcessingFailed.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static Task<IExecutionResult> ExecuteBatchAsync(IRequestExecutor executor)
    {
        var request = OperationRequestBuilder.New()
            .SetDocument("query($id: String!) { item(id: $id) { id ... @defer { stubborn } } }")
            .SetVariableValues(
                new IReadOnlyDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["id"] = "1" },
                    new Dictionary<string, object?> { ["id"] = "2" }
                })
            .Build();

        return executor
            .ExecuteAsync(request, TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static ResponseStream[] GetStreams(IExecutionResult result)
        => [.. Assert.IsType<OperationResultBatch>(result).Results.Select(Assert.IsType<ResponseStream>)];

    private static async Task ReadInitialPayloadAsync(ResponseStream stream)
    {
        await using var enumerator = stream.ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var hasPayload = await enumerator.MoveNextAsync()
            .AsTask()
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        Assert.True(hasPayload);
        await enumerator.Current.DisposeAsync();
    }

    private static async Task<int> ReadToCompletionAsync(ResponseStream stream)
    {
        var count = 0;

        await foreach (var payload in stream.ReadResultsAsync()
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            count++;
            await payload.DisposeAsync();
        }

        return count;
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(Probe probe)
        => new ServiceCollection()
            .AddSingleton(probe)
            .AddSingleton<ObjectPool<OperationContext>>(
                sp => new TrackingOperationContextPool(sp.GetRequiredService<IFactory<OperationContext>>(), probe))
            .AddGraphQL()
            .AddQueryType<Query>()
            .AddDiagnosticEventListener(_ => new ProcessingListener(probe))
            .ModifyOptions(options => options.EnableDefer = true)
            .BuildRequestExecutorAsync();

    public sealed class Query
    {
        public Item GetItem(string id) => new(id);
    }

    public sealed class Item(string id)
    {
        public string Id => id;

        public async Task<string> GetStubbornAsync([Service] Probe probe)
        {
            probe.Started(id).TrySetResult();
            await probe.Gate(id).Task;
            probe.Returned(id).TrySetResult();
            return $"stubborn {id}";
        }
    }

    public sealed class Probe
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _sources = new();
        private int _itemContextsReturned;
        private int _failProcessing;

        public string PoolState => $"item contexts returned: {Volatile.Read(ref _itemContextsReturned)}";

        public TaskCompletionSource ProcessingFailed { get; } = Create();

        public bool IsProcessingFailing => Volatile.Read(ref _failProcessing) is 1;

        public void FailProcessing() => Volatile.Write(ref _failProcessing, 1);

        public TaskCompletionSource Gate(string id) => Get("gate", id);

        public TaskCompletionSource Started(string id) => Get("started", id);

        public TaskCompletionSource Returned(string id) => Get("returned", id);

        public void ItemContextReturned() => Interlocked.Increment(ref _itemContextsReturned);

        private TaskCompletionSource Get(string kind, string id)
            => _sources.GetOrAdd($"{kind}:{id}", static _ => Create());

        private static TaskCompletionSource Create()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // The operation contexts of the batch items are rented first, the contexts of
    // deferred branches are rented later and are returned by their tasks.
    private sealed class TrackingOperationContextPool(IFactory<OperationContext> factory, Probe probe)
        : ObjectPool<OperationContext>
    {
        private readonly ConcurrentDictionary<OperationContext, bool> _items = new(ReferenceEqualityComparer.Instance);
        private int _rented;

        public override OperationContext Get()
        {
            var context = factory.Create();

            if (Interlocked.Increment(ref _rented) <= ItemCount)
            {
                _items.TryAdd(context, true);
            }

            return context;
        }

        public override void Return(OperationContext obj)
        {
            if (_items.ContainsKey(obj))
            {
                probe.ItemContextReturned();
            }
        }
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
}
