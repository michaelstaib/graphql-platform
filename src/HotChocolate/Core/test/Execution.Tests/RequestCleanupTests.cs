using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HotChocolate.Execution.Instrumentation;

namespace HotChocolate.Execution.Processing;

public class RequestCleanupTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ExecuteAsync_Should_RunActionsInRegistrationOrderAndLastAfterAll_When_LastHolderReleases()
    {
        // arrange
        var log = new List<string>();
        var cleanup = new RequestCleanup();
        cleanup.RegisterLast(new LoggingDisposable(log, "last"));
        cleanup.Register(Log(log, "first"));
        cleanup.Register(new LoggingDisposable(log, "second"));
        cleanup.Register(Log(log, "third"));

        // act
        await cleanup.ExecuteAsync();

        // assert
        log.MatchInlineSnapshots(["first", "second", "third", "last"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_KeepActionsPending_When_AnotherHolderIsActive()
    {
        // arrange
        var log = new List<string>();
        var cleanup = new RequestCleanup();
        cleanup.Register(Log(log, "action"));
        cleanup.Hold();

        // act
        await cleanup.ExecuteAsync();
        var pending = log.ToArray();
        await cleanup.ReleaseAsync();

        // assert
        Assert.Empty(pending);
        log.MatchInlineSnapshots(["action"]);
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
    public async Task ExecuteAsync_Should_ThrowAllFailuresAfterRunningAllActions_When_ActionsFail()
    {
        // arrange
        var log = new List<string>();
        var cleanup = new RequestCleanup();
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("first failure")));
        cleanup.Register(Log(log, "between"));
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("second failure")));

        // act
        var exception = await Assert.ThrowsAsync<AggregateException>(() => cleanup.ExecuteAsync().AsTask());

        // assert
        exception.InnerExceptions.Select(t => t.Message).ToArray()
            .MatchInlineSnapshots(["first failure", "second failure"]);
        log.MatchInlineSnapshots(["between"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ThrowTheFailureAsIs_When_OneActionFails()
    {
        // arrange
        var cleanup = new RequestCleanup();
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("only failure")));

        // act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.ExecuteAsync().AsTask());

        // assert
        Assert.Equal("only failure", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotReportFailuresToDiagnosticEvents_When_ActionsFail()
    {
        // arrange
        var listener = new RecordingListener();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            listener);
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("first failure")));
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("second failure")));

        // act
        var exception = await Assert.ThrowsAsync<AggregateException>(() => cleanup.ExecuteAsync().AsTask());

        // assert
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Empty(listener.Errors);
    }

    [Fact]
    public async Task ReleaseAsync_Should_ReportFailureToDiagnosticEvents_When_LastHolderIsBackground()
    {
        // arrange
        var listener = new RecordingListener();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            listener);
        cleanup.Hold();
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("cleanup failed")));
        await cleanup.ExecuteAsync();

        // act
        await cleanup.ReleaseAsync();

        // assert
        listener.Errors.ToArray().MatchInlineSnapshots(["cleanup failed"]);
    }

    [Fact]
    public async Task ReleaseAsync_Should_ReportEveryFailureToDiagnosticEvents_When_LastHolderIsBackground()
    {
        // arrange
        var listener = new RecordingListener();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            listener);
        cleanup.Hold();
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("first failure")));
        cleanup.Register(() => ValueTask.FromException(new InvalidOperationException("second failure")));
        await cleanup.ExecuteAsync();

        // act
        await cleanup.ReleaseAsync();

        // assert
        listener.Errors.ToArray().MatchInlineSnapshots(["first failure", "second failure"]);
    }

    [Fact]
    public async Task Cancel_Should_NotCancelSource_When_CollectorWasExecuted()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            new RecordingListener());
        await cleanup.ExecuteAsync();

        // act
        cleanup.Cancel();

        // assert
        Assert.False(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_Should_StopListeningToRequestToken_When_LastHolderReleases()
    {
        // arrange
        using var requestAborted = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            requestAborted.Token,
            new PooledRequestContext(),
            new RecordingListener());
        await cleanup.ExecuteAsync();

        // act
        await requestAborted.CancelAsync();

        // assert
        Assert.False(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task DetachCancellation_Should_StopListeningToRequestToken_When_CollectorIsStillHeld()
    {
        // arrange
        using var requestAborted = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            requestAborted.Token,
            new PooledRequestContext(),
            new RecordingListener());

        // act
        cleanup.DetachCancellation();
        await requestAborted.CancelAsync();

        // assert
        Assert.False(cancellation.IsCancellationRequested);
        await cleanup.ExecuteAsync();
    }

    [Fact]
    public async Task Constructor_Should_CancelSource_When_RequestTokenIsCanceled()
    {
        // arrange
        using var requestAborted = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            requestAborted.Token,
            new PooledRequestContext(),
            new RecordingListener());

        // act
        await requestAborted.CancelAsync();

        // assert
        Assert.True(cancellation.IsCancellationRequested);
        await cleanup.ExecuteAsync();
    }

    [Fact]
    public async Task DisposeAsync_Should_CancelSourceAndKeepActionsPending_When_ExecutionOutlivesTheStream()
    {
        // arrange
        var log = new List<string>();
        var execution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            new RecordingListener());
        cleanup.Register(Log(log, "action"));
        cleanup.AttachExecution(execution.Task);

        // act
        await cleanup.DisposeAsync();
        var pending = log.ToArray();
        execution.SetException(new InvalidOperationException("aborted"));
        await WaitForAsync(() => log.Count is 1);

        // assert
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Empty(pending);
        log.MatchInlineSnapshots(["action"]);
    }

    [Fact]
    public async Task DisposeAsync_Should_RunActionsWithoutCancellation_When_ExecutionCompleted()
    {
        // arrange
        var log = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            new RecordingListener());
        cleanup.Register(Log(log, "action"));
        cleanup.AttachExecution(Task.CompletedTask);

        // act
        await cleanup.DisposeAsync();

        // assert
        Assert.False(cancellation.IsCancellationRequested);
        log.MatchInlineSnapshots(["action"]);
    }

    [Fact]
    public async Task DisposeAsync_Should_ObserveFault_When_ExecutionFaultedBeforeDisposal()
    {
        // arrange
        const string message = "execution faulted before disposal";
        var unobserved = new ConcurrentQueue<string>();

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            foreach (var exception in args.Exception.InnerExceptions)
            {
                unobserved.Enqueue(exception.Message);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;

        try
        {
            // act
            await DisposeWithFaultedExecutionAsync(message);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // assert
            Assert.Equal(0, unobserved.Count(value => value == message));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Fact]
    public async Task DisposeAsync_Should_RunActionsAndRethrow_When_CancellationCallbackThrows()
    {
        // arrange
        var log = new List<string>();
        var execution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        cancellation.Token.Register(() => throw new InvalidOperationException("callback failed"));
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            new RecordingListener());
        cleanup.Register(Log(log, "action"));
        cleanup.AttachExecution(execution.Task);

        // act
        var exception = await Assert.ThrowsAsync<AggregateException>(() => cleanup.DisposeAsync().AsTask());
        execution.SetResult();
        await WaitForAsync(() => log.Count is 1);

        // assert
        Assert.Equal("callback failed", Assert.Single(exception.InnerExceptions).Message);
        log.MatchInlineSnapshots(["action"]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task DisposeWithFaultedExecutionAsync(string message)
    {
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RequestCleanup(
            cancellation,
            CancellationToken.None,
            new PooledRequestContext(),
            new RecordingListener());
        cleanup.AttachExecution(Task.FromException(new InvalidOperationException(message)));

        await cleanup.DisposeAsync();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_timeout);

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static Func<ValueTask> Log(List<string> log, string name)
        => () =>
        {
            log.Add(name);
            return ValueTask.CompletedTask;
        };

    private sealed class LoggingDisposable(List<string> log, string name) : IDisposable
    {
        public void Dispose() => log.Add(name);
    }

    private sealed class RecordingListener : ExecutionDiagnosticEventListener
    {
        public ConcurrentQueue<string> Errors { get; } = new();

        public override void RequestError(RequestContext context, Exception error)
            => Errors.Enqueue(error.Message);
    }
}
