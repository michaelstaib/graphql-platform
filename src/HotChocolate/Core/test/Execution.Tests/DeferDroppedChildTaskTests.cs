using System.Collections;
using System.Text.Json;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class DeferDroppedChildTaskTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Execute_Should_ReturnContextsToTheirPool_When_ResolverListFailsAfterAChildTaskWasCreated()
    {
        // arrange
        var pool = new TrackingOperationContextPool();
        var executor = await CreateExecutorAsync(pool);

        // act
        var payloads = await ExecuteAsync(executor, "{ plain ... @defer { items { slow } } }");
        await pool.WaitForReturnsAsync(2);

        // assert
        GetCompletedErrors(payloads).MatchInlineSnapshot("Unexpected Execution Error at items");
        Assert.Equal([true, true], pool.Reusable);
    }

    [Fact]
    public async Task Execute_Should_ReturnContextsToTheirPool_When_BatchListFailsAfterAChildTaskWasCreated()
    {
        // arrange
        var pool = new TrackingOperationContextPool();
        var executor = await CreateExecutorAsync(pool);

        // act
        var payloads = await ExecuteAsync(executor, "{ plain ... @defer { batchItems { slow } } }");
        await pool.WaitForReturnsAsync(2);

        // assert
        GetCompletedErrors(payloads).MatchInlineSnapshot("Unexpected Execution Error at batchItems");
        Assert.Equal([true, true], pool.Reusable);
    }

    [Fact]
    public async Task Execute_Should_ReturnContextsToTheirPool_When_DroppedChildrenIncludeADeferredFragment()
    {
        // arrange
        var pool = new TrackingOperationContextPool();
        var executor = await CreateExecutorAsync(pool);

        // act
        var payloads = await ExecuteAsync(
            executor,
            "{ plain ... @defer { items { slow ... @defer { later } } } }");
        await pool.WaitForReturnsAsync(3);

        // assert
        GetCompletedErrors(payloads).MatchInlineSnapshot("Unexpected Execution Error at items");
        Assert.Equal([true, true, true], pool.Reusable);
    }

    private static string GetCompletedErrors(List<string> payloads)
    {
        var errors = new List<string>();

        foreach (var payload in payloads)
        {
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("completed", out var completed))
            {
                continue;
            }

            foreach (var entry in completed.EnumerateArray())
            {
                foreach (var error in entry.GetProperty("errors").EnumerateArray())
                {
                    var path = string.Join('.', error.GetProperty("path").EnumerateArray().Select(p => p.ToString()));
                    errors.Add($"{error.GetProperty("message").GetString()} at {path}");
                }
            }
        }

        return string.Join("; ", errors);
    }

    private static async Task<List<string>> ExecuteAsync(IRequestExecutor executor, string query)
    {
        return await Task.Run(
                async () =>
                {
                    var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    var payloads = new List<string>();

                    await using var stream = Assert.IsType<ResponseStream>(result);

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

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(TrackingOperationContextPool pool)
    {
        var builder = new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor =>
                {
                    descriptor.Field("plain").Type<StringType>().Resolve("plain");
                    descriptor
                        .Field("items")
                        .Type("[Item]!")
                        .Resolve(_ => new FailingList());
                    descriptor
                        .Field("batchItems")
                        .Type("[Item]!")
                        .ResolveBatch(ResolveBatchItems);
                })
            .AddObjectType<Item>()
            .ModifyOptions(options => options.EnableDefer = true);

        builder.Services.AddSingleton<IFactory<OperationContextOwner>>(
            sp => new OperationContextOwnerFactory(pool.Wrap(sp.GetRequiredService<ObjectPool<OperationContext>>())));

        return builder.BuildRequestExecutorAsync();
    }

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatchItems(
        IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok(new FailingList());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    public sealed class Item
    {
        public async Task<string> GetSlowAsync()
        {
            await Task.Yield();
            return "slow";
        }

        public string Later => "later";
    }

    private sealed class FailingList : IList
    {
        private readonly Item _item = new();

        public object? this[int index]
        {
            get => index is 0 ? _item : throw new InvalidOperationException("The second item failed.");
            set => throw new NotSupportedException();
        }

        public int Count => 2;

        public bool IsReadOnly => true;

        public bool IsFixedSize => true;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public int Add(object? value) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(object? value) => throw new NotSupportedException();

        public void CopyTo(Array array, int index) => throw new NotSupportedException();

        public IEnumerator GetEnumerator() => throw new NotSupportedException();

        public int IndexOf(object? value) => throw new NotSupportedException();

        public void Insert(int index, object? value) => throw new NotSupportedException();

        public void Remove(object? value) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();
    }

    private sealed class TrackingOperationContextPool
    {
        private readonly List<bool> _reusable = [];
        private readonly SemaphoreSlim _returned = new(0);

        public IReadOnlyList<bool> Reusable
        {
            get
            {
                lock (_reusable)
                {
                    return [.. _reusable];
                }
            }
        }

        public ObjectPool<OperationContext> Wrap(ObjectPool<OperationContext> inner)
            => new Pool(this, inner);

        public async Task WaitForReturnsAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (!await _returned.WaitAsync(s_timeout, TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException("A context was not returned to its pool in time.");
                }
            }
        }

        private sealed class Pool(TrackingOperationContextPool owner, ObjectPool<OperationContext> inner)
            : ObjectPool<OperationContext>
        {
            public override OperationContext Get() => inner.Get();

            public override void Return(OperationContext obj)
            {
                inner.Return(obj);

                lock (owner._reusable)
                {
                    // a context the pool accepted for reuse was cleaned.
                    owner._reusable.Add(!obj.IsInitialized);
                }

                owner._returned.Release();
            }
        }
    }
}
