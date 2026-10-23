namespace HotChocolate.Text.Json;

public class ResultDocumentLifetimeTests
{
    [Fact]
    public void Finalizer_Should_NotThrow_When_ConstructionFailedBecauseTheArenaWasDisposed()
    {
        // arrange
        var operation = CommonTestExtensions.CreateOperation();
        var arena = CommonTestExtensions.CreateArena();
        ((IDisposable)arena).Dispose();

        // act
        // an exception thrown by the finalizer terminates the test process.
        var exception = Record.Exception(() => new ResultDocument(arena, operation, 0));
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // assert
        Assert.IsType<ObjectDisposedException>(exception);
    }
}
