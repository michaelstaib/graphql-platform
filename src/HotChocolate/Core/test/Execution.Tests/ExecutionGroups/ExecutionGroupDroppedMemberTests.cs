using System.Collections.Concurrent;
using HotChocolate.Execution.Processing.Tasks;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution.ExecutionGroups;

public class ExecutionGroupDroppedMemberTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Execute_Should_RunLaterMemberAndReturnTasks_When_NonNullParentOfAnEarlierMemberIsNulled()
    {
        // arrange
        // boom nulls the non-null pageInfo after its hasNextPage member task was created, so that task is dropped
        var script = new ExecutionScript();
        var pool = new CountingResolverTaskPool();
        var executor = await CreateExecutorAsync(script, pool);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("strict.nodes");

        // act
        var result = await executor.ExecuteAsync(
            "{ strict { nodes pageInfo { hasNextPage boom } } }",
            cts.Token);
        await pool.WaitForAllReturnedAsync(cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start strict.nodes",
            "end strict.nodes"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Unexpected Execution Error",
                  "path": [
                    "strict",
                    "pageInfo"
                  ]
                }
              ],
              "data": {
                "strict": null
              }
            }
            """);
        Assert.Equal(pool.Rented, pool.Returned);
    }

    [Fact]
    public async Task Execute_Should_CompleteAndReturnTasks_When_NonNullGroupRootIsNulledAfterMemberTasksWereCreated()
    {
        // arrange
        // boom nulls the non-null group root after its member tasks were created, so they are dropped
        var script = new ExecutionScript();
        var pool = new CountingResolverTaskPool();
        var executor = await CreateExecutorAsync(script, pool);
        using var cts = new CancellationTokenSource(s_timeout);

        // act
        var result = await executor.ExecuteAsync(
            "{ required { nodes pageInfo { hasNextPage } boom } }",
            cts.Token);
        await pool.WaitForAllReturnedAsync(cts.Token);

        // assert
        Assert.Empty(script.Events);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Unexpected Execution Error",
                  "path": [
                    "required"
                  ]
                }
              ],
              "data": null
            }
            """);
        Assert.Equal(pool.Rented, pool.Returned);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(
        ExecutionScript script,
        CountingResolverTaskPool pool)
    {
        var filterFailed = 0;
        var builder = new ServiceCollection()
            .AddSingleton<ObjectPool<ResolverTask>>(pool)
            .AddGraphQL()
            .AddQueryType(descriptor =>
            {
                descriptor.Name("Query");
                descriptor
                    .Field("strict")
                    .Type<ObjectType<StrictConnection>>()
                    .Resolve(_ => new StrictConnection())
                    .ExecutionGroup(
                        new ExecutionGroupEntry("pageInfo.hasNextPage", 0, isStreamable: false),
                        new ExecutionGroupEntry("nodes", 1, isStreamable: false));
                descriptor
                    .Field("required")
                    .Type<NonNullType<ObjectType<RequiredConnection>>>()
                    .Resolve(_ => new RequiredConnection())
                    .ExecutionGroup(
                        new ExecutionGroupEntry("nodes", 0, isStreamable: false),
                        new ExecutionGroupEntry("pageInfo.hasNextPage", 1, isStreamable: false));
            })
            .AddObjectType<StrictConnection>(descriptor =>
            {
                descriptor.Name("StrictConnection");
                descriptor
                    .Field("nodes")
                    .Type<ListType<StringType>>()
                    .Resolve(ctx => script.RunAsync(ctx, new[] { "n" }));
                descriptor
                    .Field("pageInfo")
                    .Type<NonNullType<ObjectType<PageInfo>>>()
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo()));
            })
            .AddObjectType<RequiredConnection>(descriptor =>
            {
                descriptor.Name("RequiredConnection");
                descriptor
                    .Field("nodes")
                    .Type<ListType<StringType>>()
                    .Resolve(ctx => script.RunAsync(ctx, new[] { "n" }));
                descriptor
                    .Field("pageInfo")
                    .Type<ObjectType<PageInfo>>()
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo()));
                descriptor.Field(c => c.Boom);
            })
            .AddObjectType<PageInfo>(descriptor =>
            {
                descriptor.Name("PageInfo");
                descriptor
                    .Field("hasNextPage")
                    .Type<BooleanType>()
                    .Resolve(ctx => script.RunAsync(ctx, true));
                descriptor.Field(p => p.Boom);
            })
            // the first error of boom faults the completion of its parent, which then is nulled.
            .AddErrorFilter(
                error => error.Exception?.Message is "boom" && Interlocked.Exchange(ref filterFailed, 1) == 0
                    ? throw new InvalidOperationException("The error filter failed.")
                    : error);

        return builder.BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    internal sealed class StrictConnection;

    internal sealed class RequiredConnection
    {
        public string? Boom => throw new InvalidOperationException("boom");
    }

    internal sealed class PageInfo
    {
        public string? Boom => throw new InvalidOperationException("boom");
    }

    private sealed class CountingResolverTaskPool : ObjectPool<ResolverTask>
    {
        private readonly ConcurrentBag<ResolverTask> _tasks = [];
        private readonly SemaphoreSlim _returned = new(0);
        private int _rented;
        private int _returnedCount;

        public int Rented => Volatile.Read(ref _rented);

        public int Returned => Volatile.Read(ref _returnedCount);

        public override ResolverTask Get()
        {
            Interlocked.Increment(ref _rented);
            return _tasks.TryTake(out var task) ? task : new ResolverTask(this);
        }

        public override void Return(ResolverTask obj)
        {
            obj.Reset();
            _tasks.Add(obj);
            Interlocked.Increment(ref _returnedCount);
            _returned.Release();
        }

        public async Task WaitForAllReturnedAsync(CancellationToken cancellationToken)
        {
            // no task is rented after the result was produced, so the counts only converge.
            while (Returned < Rented)
            {
                await _returned.WaitAsync(cancellationToken);
            }
        }
    }
}
