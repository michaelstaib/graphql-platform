using HotChocolate.Execution.Processing.Tasks;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class NonNullInvalidatedObjectTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Execute_Should_SkipAndReleaseChildResolvers_When_PureChildInvalidatesResolvedObject()
    {
        // arrange
        var counter = new SlowCounter();
        var pool = new CountingResolverTaskPool();
        var executor = await CreateExecutorAsync(counter, pool);

        // act
        var result = await executor.ExecuteAsync(
            "{ holder { asyncObj { pureNull slow } } }",
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "holder",
                    "asyncObj",
                    "pureNull"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "holder": null
              }
            }
            """);
        Assert.Equal(0, counter.Count);
        await pool.WaitUntilAllReturnedAsync();
    }

    [Fact]
    public async Task Execute_Should_ReportOnlyTheChildViolation_When_PureChildInvalidatesPureObject()
    {
        // arrange
        var executor = await CreateExecutorAsync(new SlowCounter(), new CountingResolverTaskPool());

        // act
        var result = await executor.ExecuteAsync(
            "{ holder { pureObj { pureNull slow } } }",
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "holder",
                    "pureObj",
                    "pureNull"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "holder": null
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_SkipAndReleaseChildResolvers_When_PureChildInvalidatesBatchObject()
    {
        // arrange
        var counter = new SlowCounter();
        var pool = new CountingResolverTaskPool();
        var executor = await CreateExecutorAsync(counter, pool);

        // act
        var result = await executor.ExecuteAsync(
            "{ holder { batchObj { pureNull slow } } }",
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "holder",
                    "batchObj",
                    "pureNull"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "holder": null
              }
            }
            """);
        Assert.Equal(0, counter.Count);
        await pool.WaitUntilAllReturnedAsync();
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(
        SlowCounter counter,
        CountingResolverTaskPool pool)
    {
        return new ServiceCollection()
            .AddSingleton(counter)
            .AddSingleton<ObjectPool<ResolverTask>>(pool)
            .AddGraphQL()
            .AddQueryType<Query>()
            .AddObjectType<Holder>(
                descriptor => descriptor
                    .Field("batchObj")
                    .Type<NonNullType<ObjectType<Obj>>>()
                    .ResolveBatch(ResolveBatchObj))
            .BuildRequestExecutorAsync();
    }

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatchObj(
        IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok(new Obj());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    public sealed class Query
    {
        public async Task<Holder?> GetHolderAsync()
        {
            await Task.Yield();
            return new Holder();
        }
    }

    public sealed class Holder
    {
        [GraphQLNonNullType]
        public Obj PureObj => new();

        [GraphQLNonNullType]
        public async Task<Obj> GetAsyncObjAsync()
        {
            await Task.Yield();
            return new Obj();
        }
    }

    public sealed class Obj
    {
        [GraphQLNonNullType]
        public string? PureNull => null;

        public async Task<string> GetSlowAsync([Service] SlowCounter counter)
        {
            counter.Increment();
            await Task.Yield();
            return "slow";
        }
    }

    public sealed class SlowCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingResolverTaskPool : ObjectPool<ResolverTask>
    {
        private readonly SemaphoreSlim _returned = new(0);
        private int _outstanding;

        public override ResolverTask Get()
        {
            Interlocked.Increment(ref _outstanding);
            return new ResolverTask(this);
        }

        public override void Return(ResolverTask obj)
        {
            if (Interlocked.Decrement(ref _outstanding) == 0)
            {
                _returned.Release();
            }
        }

        public async Task WaitUntilAllReturnedAsync()
        {
            while (Volatile.Read(ref _outstanding) != 0)
            {
                if (!await _returned.WaitAsync(s_timeout, TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException("A resolver task was not returned to its pool in time.");
                }
            }
        }
    }
}
