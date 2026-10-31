using System.Runtime.CompilerServices;
using HotChocolate.Execution;

namespace HotChocolate.Text.Json;

public class ResultDocumentRemovedContainerTests
{
    [Fact]
    public void Parent_Should_ReachTheRemovedList_When_ListWasNulledAfterItsItemsWereCompleted()
    {
        // arrange
        var operation = StreamBatchDocumentFactory.CreateOperation();
        using var document = StreamBatchDocumentFactory.Create(
            operation,
            startIndex: 0,
            itemCount: 2,
            characterCount: 1);
        var list = document.Data.GetProperty("films");
        var characters = list[0].GetProperty("characters");

        // act
        list.SetNullValue();

        // assert
        Assert.True(characters.Parent.Parent.IsNullOrInvalidated);
    }

    [Fact]
    public void Dispose_Should_NotThrow_When_DocumentWasNeverCreated()
    {
        // arrange
        var document = (ResultDocument)RuntimeHelpers.GetUninitializedObject(typeof(ResultDocument));

        // act
        var exception = Record.Exception(document.Dispose);

        // assert
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_Should_ThrowOriginalError_When_ArenaIsDisposed()
    {
        // arrange
        var operation = StreamBatchDocumentFactory.CreateOperation();
        var arena = CommonTestExtensions.CreateArena();
        ((IDisposable)arena).Dispose();

        // act
        var exception = Record.Exception(
            () => new ResultDocument(
                arena,
                operation,
                operation.RootSelectionSet.Selections[0],
                Path.Root,
                0,
                new ConditionFlags(0)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // assert
        Assert.IsType<ObjectDisposedException>(exception);
    }
}
