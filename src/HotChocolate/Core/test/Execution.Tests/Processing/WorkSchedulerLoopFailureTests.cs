using GreenDonut;
using HotChocolate.Fetching;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotChocolate.Execution.Processing;

public class WorkSchedulerLoopFailureTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ExecuteAsync_Should_KeepContextIntact_When_TheDispatcherFailsWhileAResolverRuns()
    {
        // arrange
        var resolverReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolverFinished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new FailingBatchDispatcher();
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor => descriptor
                    .Field("slow")
                    .Type<StringType>()
                    .Resolve(
                        async context =>
                        {
                            // the dispatcher reports an enqueued batch, so the scheduler asks it to dispatch
                            // while this resolver is still running.
                            dispatcher.ReportEnqueuedBatch();

                            await resolverReleased.Task;

                            try
                            {
                                _ = context.Operation;
                                resolverFinished.SetResult("context intact");
                            }
                            catch (Exception ex)
                            {
                                resolverFinished.SetResult(ex.GetType().Name);
                            }

                            return "done";
                        }))
            .Services
            .RemoveAll<IBatchDispatcher>()
            .AddScoped<IBatchDispatcher>(_ => dispatcher)
            .BuildServiceProvider()
            .GetRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor
            .ExecuteAsync("{ slow }", TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var resolverCompletedBeforeRelease = resolverFinished.Task.IsCompleted;
        resolverReleased.SetResult();
        var contextState = await resolverFinished.Task
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.False(resolverCompletedBeforeRelease);
        Assert.Equal("context intact", contextState);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Unexpected Execution Error"
                }
              ]
            }
            """);
    }

    private sealed class FailingBatchDispatcher : IBatchDispatcher
    {
        private IObserver<BatchDispatchEventArgs>? _observer;

        public void ReportEnqueuedBatch()
            => _observer?.OnNext(new BatchDispatchEventArgs(BatchDispatchEventType.Enqueued));

        public void Schedule(Batch batch)
        {
        }

        public IDisposable Subscribe(IObserver<BatchDispatchEventArgs> observer)
        {
            _observer = observer;
            return new Subscription();
        }

        public void BeginDispatch(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The dispatcher failed.");

        public void Dispose()
        {
        }

        private sealed class Subscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
