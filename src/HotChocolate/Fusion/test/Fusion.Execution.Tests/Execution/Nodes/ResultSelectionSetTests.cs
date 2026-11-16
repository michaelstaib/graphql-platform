using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Execution.Nodes;

public sealed class ResultSelectionSetTests : FusionTestBase
{
    [Theory]
    [InlineData("{ a { x } b { y } c { z } }")]
    [InlineData("{ f1 f2 f3 f4 f5 f6 f7 a { x } b { y } c { z } }")]
    public void TryGetChild_Should_ReturnChildAtPosition_When_PositionHoldsResponseName(string selectionSet)
    {
        // arrange
        var resultSelectionSet = Create(selectionSet);
        var position = resultSelectionSet.Selections.Length - 2;

        // act
        var child = resultSelectionSet.TryGetChild("b", position);

        // assert
        Assert.Equal("{ y }", child?.ToString());
    }

    [Theory]
    [InlineData("{ a { x } b { y } c { z } }", -1)]
    [InlineData("{ a { x } b { y } c { z } }", 0)]
    [InlineData("{ a { x } b { y } c { z } }", 2)]
    [InlineData("{ a { x } b { y } c { z } }", 3)]
    [InlineData("{ f1 f2 f3 f4 f5 f6 f7 a { x } b { y } c { z } }", 0)]
    [InlineData("{ f1 f2 f3 f4 f5 f6 f7 a { x } b { y } c { z } }", 10)]
    public void TryGetChild_Should_SearchResponseName_When_PositionHoldsAnotherSelection(
        string selectionSet,
        int position)
    {
        // arrange
        var resultSelectionSet = Create(selectionSet);

        // act
        var child = resultSelectionSet.TryGetChild("b", position);

        // assert
        Assert.Equal("{ y }", child?.ToString());
    }

    [Fact]
    public void TryGetChild_Should_ReturnNull_When_ResponseNameIsNotSelected()
    {
        // arrange
        var resultSelectionSet = Create("{ a { x } b { y } }");

        // act
        var child = resultSelectionSet.TryGetChild("c", position: 1);

        // assert
        Assert.Null(child);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TryGetChild_Should_ReturnSearchedChild_When_ResponseNameRepeats(int position)
    {
        // arrange
        var resultSelectionSet = Create("{ a { x } a { y } }");

        // act
        var child = resultSelectionSet.TryGetChild("a", position);

        // assert
        Assert.Same(resultSelectionSet.TryGetChild("a"), child);
    }

    [Theory]
    [InlineData("Item", 0, "{ y }")]
    [InlineData("Item", 1, "{ y }")]
    [InlineData("Other", 0, null)]
    [InlineData("Item", 2, "{ y }")]
    public void TryGetChild_Should_ApplyTypeCondition_When_ResponseNameIsInFragment(
        string typeName,
        int position,
        string? expected)
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: test
            type Query {
              node: Node
            }
            interface Node {
              a: Child
            }
            type Item implements Node {
              a: Child
              b: Child
            }
            type Other implements Node {
              a: Child
            }
            type Child {
              x: String
              y: String
            }
            """);
        var resultSelectionSet = ResultSelectionSet.Create(
            Utf8GraphQLParser.Syntax.ParseSelectionSet("{ a { x } ... on Item { b { y } } }"),
            schema);
        var objectType = schema.Types.GetType<IObjectTypeDefinition>(typeName);

        // act
        var child = resultSelectionSet.TryGetChild("b", objectType, position);

        // assert
        Assert.Equal(expected, child?.ToString());
    }

    private static ResultSelectionSet Create(string selectionSet)
        => ResultSelectionSet.Create(Utf8GraphQLParser.Syntax.ParseSelectionSet(selectionSet));
}
