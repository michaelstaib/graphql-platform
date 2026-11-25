using System.Collections.Concurrent;

namespace HotChocolate.Execution.Processing;

public class RequestCleanupTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ExecuteAsync_Should_RunActionsInRegistrationOrder_When_ActionsAreRegistered()
    {
        // arrange
        var log = new List<string>();
        var cleanup = new RequestCleanup();
        cleanup.Register(Log(log, "first"));
        cleanup.Register(Log(log, "second"));
        cleanup.Register(Log(log, "third"));

        // act
        await cleanup.ExecuteAsync();

        // assert
        log.MatchInlineSnapshots(["first", "second", "third"]);
    }

    [Fact]
    public async Task Register_Should_RunActionImmediately_When_CollectorWasExecuted()
    {
        // arrange
        var log = new ConcurrentQueue<string>();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new RequestCleanup();
        await cleanup.ExecuteAsync();

        // act
        cleanup.Register(
            () =>
            {
                log.Enqueue("late");
                ran.SetResult();
                return ValueTask.CompletedTask;
            });
        await ran.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        log.ToArray().MatchInlineSnapshots(["late"]);
    }

    [Fact]
    public async Task Register_Should_RunEveryActionOnce_When_RegisteredConcurrentlyWithExecute()
    {
        // arrange
        const int count = 2000;
        var counts = new int[count];
        var completed = new CountdownEvent(count);
        var cleanup = new RequestCleanup();
        var start = new ManualResetEventSlim();

        var registrations = Enumerable.Range(0, count)
            .Select(
                i => Task.Run(
                    () =>
                    {
                        start.Wait();
                        cleanup.Register(
                            () =>
                            {
                                Interlocked.Increment(ref counts[i]);
                                completed.Signal();
                                return ValueTask.CompletedTask;
                            });
                    }))
            .ToArray();

        // act
        start.Set();
        await cleanup.ExecuteAsync();
        await Task.WhenAll(registrations);
        var finished = completed.Wait(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.True(finished);
        Assert.All(counts, value => Assert.Equal(1, value));
    }

    [Fact]
    public async Task ExecuteAsync_Should_ThrowFirstFailureAfterRunningAllActions_When_ActionsFail()
    {
        // arrange
        var log = new List<string>();
        var cleanup = new RequestCleanup();
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("first failure")));
        cleanup.Register(Log(log, "between"));
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("second failure")));

        // act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.ExecuteAsync().AsTask());

        // assert
        Assert.Equal("first failure", exception.Message);
        log.MatchInlineSnapshots(["between"]);
    }

    [Fact]
    public void Cancel_Should_CancelSource_When_CollectorHasCancellation()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(cancellation, CancellationToken.None);

        // act
        cleanup.Cancel();

        // assert
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Constructor_Should_CancelSource_When_RequestTokenIsCanceled()
    {
        // arrange
        using var requestAborted = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();
        _ = new RequestCleanup(cancellation, requestAborted.Token);

        // act
        requestAborted.Cancel();

        // assert
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_Should_StopListeningToRequestToken_When_CollectorIsExecuted()
    {
        // arrange
        using var requestAborted = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(cancellation, requestAborted.Token);
        await cleanup.ExecuteAsync();

        // act
        requestAborted.Cancel();

        // assert
        Assert.False(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void DetachCancellation_Should_StopListeningToRequestToken_When_CollectorIsNotExecuted()
    {
        // arrange
        using var requestAborted = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(cancellation, requestAborted.Token);
        cleanup.DetachCancellation();

        // act
        requestAborted.Cancel();

        // assert
        Assert.False(cancellation.IsCancellationRequested);
    }

    private static Func<ValueTask> Log(List<string> log, string name)
        => () =>
        {
            log.Add(name);
            return ValueTask.CompletedTask;
        };
}
