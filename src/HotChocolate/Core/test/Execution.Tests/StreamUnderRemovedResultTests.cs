using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Processing;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public sealed class StreamUnderRemovedResultTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Execute_Should_RemoveTheListAndStopTheStreams_When_ASiblingItemViolatesNonNull()
    {
        // arrange
        var source = new StreamSource();
        var pool = new TrackingOperationContextPool();
        var executor = await CreateExecutorAsync(source, pool);

        // act
        var payloads = await ExecuteAsync(
            executor,
            "{ name nullableObjects { id slowList @stream(initialCount: 0) } }");
        await pool.WaitForReturnsAsync(1);
        await source.WaitForDisposalAsync();
        CollectGarbage();

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "nullableObjects",
                        1
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ],
                  "data": {
                    "name": "query",
                    "nullableObjects": null
                  },
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal(source.Started, source.Disposed);
        Assert.All(pool.Reusable, Assert.True);
    }

    [Fact]
    public async Task Execute_Should_RemoveTheListAndStopTheStreams_When_ASiblingFieldViolatesNonNull()
    {
        // arrange
        var source = new StreamSource();
        var executor = await CreateExecutorAsync(source, new TrackingOperationContextPool());

        // act
        var payloads = await ExecuteAsync(
            executor,
            "{ name nullableBroken { id broken slowList @stream(initialCount: 0) } }");
        await source.WaitForDisposalAsync();
        CollectGarbage();

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "nullableBroken",
                        1,
                        "broken"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ],
                  "data": {
                    "name": "query",
                    "nullableBroken": null
                  },
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal(source.Started, source.Disposed);
    }

    [Fact]
    public async Task Execute_Should_CompleteTheDeferredFragment_When_ASiblingItemViolatesNonNull()
    {
        // arrange
        var source = new StreamSource();
        var pool = new TrackingOperationContextPool();
        var executor = await CreateExecutorAsync(source, pool);

        // act
        var payloads = await ExecuteAsync(
            executor,
            "{ name ... @defer { nullableObjects { id slowList @stream(initialCount: 0) } } }");
        await pool.WaitForReturnsAsync(2);
        await source.WaitForDisposalAsync();
        CollectGarbage();

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "name": "query"
                  },
                  "pending": [
                    {
                      "id": "2",
                      "path": []
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "incremental": [
                    {
                      "id": "2",
                      "errors": [
                        {
                          "message": "Cannot return null for non-nullable field.",
                          "path": [
                            "nullableObjects",
                            1
                          ],
                          "extensions": {
                            "code": "HC0018"
                          }
                        }
                      ],
                      "data": {
                        "nullableObjects": null
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "2"
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal(source.Started, source.Disposed);
        Assert.All(pool.Reusable, Assert.True);
    }

    [Fact]
    public async Task Execute_Should_EndTheStreamWithTheError_When_ABatchItemViolatesNonNull()
    {
        // arrange
        var source = new StreamSource();
        var pool = new TrackingOperationContextPool();
        var executor = await CreateExecutorAsync(source, pool);

        // act
        var payloads = await ExecuteAsync(
            executor,
            "{ slow streamedObjects @stream(initialCount: 0) { id slowList @stream(initialCount: 0) } }");
        await pool.WaitForReturnsAsync(2);
        CollectGarbage();

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "slow": "slow",
                    "streamedObjects": []
                  },
                  "pending": [
                    {
                      "id": "2",
                      "path": [
                        "streamedObjects"
                      ]
                    }
                  ],
                  "completed": [
                    {
                      "id": "2",
                      "errors": [
                        {
                          "message": "Cannot return null for non-nullable field.",
                          "path": [
                            "streamedObjects",
                            1
                          ],
                          "extensions": {
                            "code": "HC0018"
                          }
                        }
                      ]
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal(0, source.Resolved);
        Assert.Equal([true, true], pool.Reusable);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task<List<string>> ExecuteAsync(IRequestExecutor executor, string query)
    {
        return await Task.Run(
                async () =>
                {
                    await using var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    var payloads = new List<string>();

                    if (result is OperationResult operationResult)
                    {
                        payloads.Add(operationResult.ToJson());
                        return payloads;
                    }

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

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(
        StreamSource source,
        TrackingOperationContextPool pool)
    {
        var builder = new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor =>
                {
                    descriptor.Name("Query");
                    descriptor.Field("name").Type<StringType>().Resolve("query");
                    descriptor
                        .Field("slow")
                        .Type<StringType>()
                        .Resolve(
                            async _ =>
                            {
                                await Task.Delay(500);
                                return "slow";
                            });
                    descriptor
                        .Field("nullableObjects")
                        .Type("[Item!]")
                        .Resolve(_ => new List<Item?> { new(1, source), null, new(3, source) });
                    descriptor
                        .Field("nullableBroken")
                        .Type("[Item!]")
                        .Resolve(_ => new List<Item?> { new(1, source), new(2, source), new(3, source) });
                    descriptor
                        .Field("streamedObjects")
                        .Type("[Item!]")
                        .Resolve(_ => source.GetObjects());
                })
            .AddObjectType<Item>(
                descriptor =>
                {
                    descriptor.BindFieldsExplicitly();
                    descriptor.Field(t => t.Id);
                    descriptor
                        .Field("broken")
                        .Type<NonNullType<StringType>>()
                        .Resolve(ctx => ctx.Parent<Item>().Id is 2 ? null : "ok");
                    descriptor
                        .Field("slowList")
                        .Type<ListType<IntType>>()
                        .Resolve(ctx => ctx.Parent<Item>().Source.GetSlowList());
                })
            .ModifyOptions(
                options =>
                {
                    options.EnableDefer = true;
                    options.EnableStream = true;
                });

        builder.Services.AddSingleton<IFactory<OperationContextOwner>>(
            sp => new OperationContextOwnerFactory(pool.Wrap(sp.GetRequiredService<ObjectPool<OperationContext>>())));

        return builder.BuildRequestExecutorAsync();
    }

    public sealed record Item(int Id, StreamSource Source);

    public sealed class StreamSource
    {
        private int _resolved;
        private int _started;
        private int _disposed;

        public int Resolved => Volatile.Read(ref _resolved);

        public int Started => Volatile.Read(ref _started);

        public int Disposed => Volatile.Read(ref _disposed);

        public async IAsyncEnumerable<Item?> GetObjects()
        {
            await Task.CompletedTask;
            yield return new Item(1, this);
            yield return null;
            yield return new Item(3, this);
        }

        public IAsyncEnumerable<int> GetSlowList()
        {
            Interlocked.Increment(ref _resolved);
            return EnumerateSlowList();
        }

        public async Task WaitForDisposalAsync()
        {
            using var timeout = new CancellationTokenSource(s_timeout);

            while (Started != Disposed)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private async IAsyncEnumerable<int> EnumerateSlowList()
        {
            Interlocked.Increment(ref _started);

            try
            {
                for (var i = 0; i < 3; i++)
                {
                    await Task.Delay(50);
                    yield return i;
                }
            }
            finally
            {
                Interlocked.Increment(ref _disposed);
            }
        }
    }
}
