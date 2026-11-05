using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace HotChocolate.Text.Json;

public class ResultDocumentInvalidationTests
{
    [Fact]
    public void IsNullOrInvalidated_Should_ReturnTrue_When_ReferencedObjectOfFieldIsInvalidated()
    {
        // arrange
        var operation = CreateOperation();
        using var document = new ResultDocument(CommonTestExtensions.CreateArena(), operation, 0);
        var field = document.Data.GetProperty("obj");
        field.SetObjectValue(operation.GetSelectionSet(operation.RootSelectionSet.Selections[0]));

        // act
        field.Invalidate();

        // assert
        Assert.True(field.IsNullOrInvalidated);
    }

    [Fact]
    public void IsNullOrInvalidated_Should_ReturnFalse_When_ReferencedObjectOfFieldIsNotInvalidated()
    {
        // arrange
        var operation = CreateOperation();
        using var document = new ResultDocument(CommonTestExtensions.CreateArena(), operation, 0);
        var field = document.Data.GetProperty("obj");

        // act
        field.SetObjectValue(operation.GetSelectionSet(operation.RootSelectionSet.Selections[0]));

        // assert
        Assert.False(field.IsNullOrInvalidated);
    }

    private static Operation CreateOperation()
    {
        var schema =
            SchemaBuilder.New()
                .AddDocumentFromString(
                    """
                    type Query {
                      obj: Obj
                    }

                    type Obj {
                      name: String
                    }
                    """)
                .Use(_ => _)
                .Create();

        return OperationCompiler.Compile("abc", Utf8GraphQLParser.Parse("{ obj { name } }"), schema);
    }
}
