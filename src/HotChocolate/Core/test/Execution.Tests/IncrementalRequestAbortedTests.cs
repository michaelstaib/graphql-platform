using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class IncrementalRequestAbortedTests
{
    private const int AbandonedRequests = 4;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ExecuteAsync_Should_CancelDeferredResolver_When_RequestIsAborted()
    {
        // arrange
        var gates = new Gates();
        var executor = await CreateExecutorAsync(gates);
        using var requestAborted = new CancellationTokenSource();
        var result = await executor.ExecuteAsync(
            "{ hero ... @defer { slow(id: \"a\") } }",
            requestAborted.Token);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await MoveNextAsync(enumerator);
            await gates.Started("a").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            await requestAborted.CancelAsync();
            await gates.Observed("a").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        // assert
        gates.Outcomes.ToArray().MatchInlineSnapshots(["a: cancelled"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteDeferredResolver_When_EarlierRequestsWereAbandoned()
    {
        // arrange
        var gates = new Gates();
        var executor = await CreateExecutorAsync(gates);

        for (var i = 0; i < AbandonedRequests; i++)
        {
            await AbandonAsync(executor, $"x{i}", gates);
        }

        var result = await executor.ExecuteAsync(
            "{ hero ... @defer { slow(id: \"b\") } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await MoveNextAsync(enumerator);
            await gates.Started("b").Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
            gates.Gate("b").TrySetResult();

            while (await MoveNextAsync(enumerator))
            {
                await enumerator.Current.DisposeAsync();
            }
        }

        // assert
        gates.Outcomes.ToArray().MatchInlineSnapshots(
            ["x0: cancelled", "x1: cancelled", "x2: cancelled", "x3: cancelled", "b: completed"]);
    }

    private static async Task AbandonAsync(IRequestExecutor executor, string id, Gates gates)
    {
        var result = await executor.ExecuteAsync(
            $"{{ hero ... @defer {{ slow(id: \"{id}\") }} }}",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        await using (stream)
        {
            await using var enumerator = stream.ReadResultsAsync()
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            await MoveNextAsync(enumerator);
            await gates.Started(id).Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        }

        await gates.Observed(id).Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static Task<bool> MoveNextAsync(IAsyncEnumerator<OperationResult> enumerator)
        => enumerator.MoveNextAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(Gates gates)
        => new ServiceCollection()
            .AddSingleton(gates)
            .AddGraphQL()
            .AddQueryType<Query>()
            .ModifyOptions(options => options.EnableDefer = true)
            .BuildRequestExecutorAsync();

    public sealed class Query
    {
        public string GetHero() => "hero";

        public async Task<string> GetSlowAsync(
            string id,
            [Service] Gates gates,
            CancellationToken cancellationToken)
        {
            gates.Started(id).TrySetResult();

            try
            {
                await gates.Gate(id).Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                gates.Outcomes.Enqueue($"{id}: cancelled");
                gates.Observed(id).TrySetResult();
                throw;
            }

            gates.Outcomes.Enqueue($"{id}: completed");
            gates.Observed(id).TrySetResult();
            return "slow";
        }
    }

    public sealed class Gates
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _sources = new();

        public ConcurrentQueue<string> Outcomes { get; } = new();

        public TaskCompletionSource Gate(string id) => Get("gate", id);

        public TaskCompletionSource Started(string id) => Get("started", id);

        public TaskCompletionSource Observed(string id) => Get("observed", id);

        private TaskCompletionSource Get(string kind, string id)
            => _sources.GetOrAdd(
                $"{kind}:{id}",
                static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
