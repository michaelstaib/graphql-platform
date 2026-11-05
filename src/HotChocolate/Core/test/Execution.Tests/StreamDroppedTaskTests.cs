using System.Collections;
using HotChocolate.Execution.DependencyInjection;
using HotChocolate.Execution.Processing;
using HotChocolate.Execution.Processing.Tasks;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

public class StreamDroppedTaskTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Execute_Should_ReturnDroppedChildTasks_When_StreamedItemCompletionFails()
    {
        // arrange
        var resolverTasks = new CountingResolverTaskPool();
        var contexts = new TrackingOperationContextPool();
        var source = new ListSource();
        var executor = await CreateExecutorAsync(resolverTasks, contexts, source);

        // act
        var payloads = await ReadToCompletionAsync(executor, source);
        await contexts.WaitForReturnsAsync(2);
        await resolverTasks.WaitUntilAllReturnedAsync();

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "lists": []
                  },
                  "pending": [
                    {
                      "id": "2",
                      "path": [
                        "lists"
                      ]
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "completed": [
                    {
                      "id": "2",
                      "errors": [
                        {
                          "message": "Unexpected Execution Error",
                          "path": [
                            "lists"
                          ]
                        }
                      ]
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
        Assert.Equal([true, true], contexts.Reusable);
    }

    private static async Task<List<string>> ReadToCompletionAsync(IRequestExecutor executor, ListSource source)
    {
        return await Task.Run(
                async () =>
                {
                    var payloads = new List<string>();
                    await using var result = await executor.ExecuteAsync(
                        "{ lists @stream(initialCount: 0) { slow } }",
                        TestContext.Current.CancellationToken);
                    var stream = Assert.IsType<ResponseStream>(result);

                    await foreach (var payload in stream
                        .ReadResultsAsync()
                        .WithCancellation(TestContext.Current.CancellationToken))
                    {
                        await using var cleanup = payload;
                        payloads.Add(payload.ToJson());

                        // the streamed list arrives after the initial payload was delivered.
                        source.Open();
                    }

                    return payloads;
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(
        CountingResolverTaskPool resolverTasks,
        TrackingOperationContextPool contexts,
        ListSource source)
    {
        var builder = new ServiceCollection()
            .AddSingleton<ObjectPool<ResolverTask>>(resolverTasks)
            .AddGraphQL()
            .AddQueryType(
                descriptor => descriptor
                    .Field("lists")
                    .Type("[[Item]]")
                    .Resolve(_ => source.GetListsAsync()))
            .AddObjectType<Item>()
            .ModifyOptions(
                options =>
                {
                    options.EnableDefer = true;
                    options.EnableStream = true;
                });

        builder.Services.AddSingleton<IFactory<OperationContextOwner>>(
            sp => new OperationContextOwnerFactory(
                contexts.Wrap(sp.GetRequiredService<ObjectPool<OperationContext>>())));

        return builder.BuildRequestExecutorAsync();
    }

    private sealed class ListSource
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _open.TrySetResult();

        public async IAsyncEnumerable<object> GetListsAsync()
        {
            await _open.Task.ConfigureAwait(false);
            yield return new FailingList();
        }
    }

    public sealed class Item
    {
        public async Task<string> GetSlowAsync()
        {
            await Task.Yield();
            return "slow";
        }
    }

    // the second item fails with a cancellation that does not belong to the stream,
    // after the first item created a child task.
    private sealed class FailingList : IList
    {
        private readonly Item _item = new();

        public object? this[int index]
        {
            get
            {
                if (index is not 0)
                {
                    throw new OperationCanceledException("The second item was cancelled.");
                }

                return _item;
            }
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
            // the pool policy resets a returned task, which releases its hold on the operation context.
            obj.Reset();

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
