namespace HotChocolate.Text.Json;

public class ResultDocumentLifetimeTests
{
    [Fact]
    public void Finalizer_Should_NotThrow_When_ConstructionFailedBecauseTheArenaWasDisposed()
    {
        // arrange
        var operation = CommonTestExtensions.CreateOperation();
        var arena = CommonTestExtensions.CreateArena();
        Assert.IsAssignableFrom<IDisposable>(arena).Dispose();

        // act
        Assert.Throws<ObjectDisposedException>(() => new ResultDocument(arena, operation, 0));

        // assert
        // an exception thrown by the finalizer terminates the test process.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
