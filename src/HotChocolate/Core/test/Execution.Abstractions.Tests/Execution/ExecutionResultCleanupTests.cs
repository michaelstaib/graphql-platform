using System.Collections.Immutable;

namespace HotChocolate.Execution;

public class ExecutionResultCleanupTests
{
    [Fact]
    public async Task DisposeAsync_Should_RunEveryCleanupAndThrowAllFailures_When_TwoCleanupsFail()
    {
        // arrange
        var ran = new List<string>();
        var result = CreateResult(ran, "first", "second");

        // act
        var exception = await Assert.ThrowsAsync<AggregateException>(() => result.DisposeAsync().AsTask());

        // assert
        Assert.Equal(["first", "after first", "second", "after second"], ran);
        Assert.Equal(["first", "second"], exception.InnerExceptions.Select(e => e.Message));
    }

    [Fact]
    public async Task DisposeAsync_Should_ThrowTheFailureAsIs_When_OneCleanupFails()
    {
        // arrange
        var ran = new List<string>();
        var result = CreateResult(ran, "only");

        // act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => result.DisposeAsync().AsTask());

        // assert
        Assert.Equal(["only", "after only"], ran);
        Assert.Equal("only", exception.Message);
    }

    [Fact]
    public async Task DisposeAsync_Should_DisposeEveryResultAndThrowAllFailures_When_TwoBatchResultsFail()
    {
        // arrange
        var ran = new List<string>();
        ImmutableList<IExecutionResult> results =
        [
            CreateResult(ran, "first"),
            CreateResult(ran, "second"),
            CreateResult(ran)
        ];
        var batch = new OperationResultBatch(results);

        // act
        var exception = await Assert.ThrowsAsync<AggregateException>(() => batch.DisposeAsync().AsTask());

        // assert
        Assert.Equal(["first", "after first", "second", "after second", "last"], ran);
        Assert.Equal(["first", "second"], exception.InnerExceptions.Select(e => e.Message));
    }

    // each failing cleanup is followed by one that records that it ran.
    private static ResponseStream CreateResult(List<string> ran, params string[] failures)
    {
        var result = new ResponseStream(() => null!);

        foreach (var failure in failures)
        {
            result.RegisterForCleanup(
                new Action(
                    () =>
                    {
                        ran.Add(failure);
                        throw new InvalidOperationException(failure);
                    }));
            result.RegisterForCleanup(() => ran.Add("after " + failure));
        }

        if (failures.Length == 0)
        {
            result.RegisterForCleanup(() => ran.Add("last"));
        }

        return result;
    }
}
