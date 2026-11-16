using System.Text;

namespace HotChocolate.Fusion.Execution.Nodes;

public sealed class SelectionSetTests : FusionTestBase
{
    private const string SmallOperation = "{ a b c }";
    private const string WideOperation = "{ a b c d e f g h i j }";

    [Theory]
    [InlineData(SmallOperation, "a", 0, 1)]
    [InlineData(SmallOperation, "c", 2, 3)]
    [InlineData(WideOperation, "a", 0, 1)]
    [InlineData(WideOperation, "j", 9, 10)]
    public void TryGetSelection_Should_ResolveSelectionAtPosition_When_PositionHoldsResponseName(
        string operation,
        string responseName,
        int position,
        int expectedPosition)
    {
        // arrange
        var selectionSet = CreateRootSelectionSet(operation);

        // act
        var found = selectionSet.TryGetSelection(Encoding.UTF8.GetBytes(responseName), ref position, out var selection);

        // assert
        Assert.True(found);
        Assert.Equal(responseName, selection!.ResponseName);
        Assert.Equal(expectedPosition, position);
    }

    [Theory]
    [InlineData(SmallOperation, "b", -1, 2)]
    [InlineData(SmallOperation, "b", 0, 2)]
    [InlineData(SmallOperation, "a", 2, 1)]
    [InlineData(SmallOperation, "c", 3, 3)]
    [InlineData(WideOperation, "h", 0, 8)]
    [InlineData(WideOperation, "a", 9, 1)]
    [InlineData(WideOperation, "d", 100, 4)]
    public void TryGetSelection_Should_SearchResponseName_When_PositionHoldsAnotherSelection(
        string operation,
        string responseName,
        int position,
        int expectedPosition)
    {
        // arrange
        var selectionSet = CreateRootSelectionSet(operation);

        // act
        var found = selectionSet.TryGetSelection(Encoding.UTF8.GetBytes(responseName), ref position, out var selection);

        // assert
        Assert.True(found);
        Assert.Equal(responseName, selection!.ResponseName);
        Assert.Equal(expectedPosition, position);
    }

    [Theory]
    [InlineData(SmallOperation, 0)]
    [InlineData(SmallOperation, 3)]
    [InlineData(WideOperation, 4)]
    public void TryGetSelection_Should_KeepPosition_When_ResponseNameIsNotSelected(string operation, int position)
    {
        // arrange
        var selectionSet = CreateRootSelectionSet(operation);
        var expectedPosition = position;

        // act
        var found = selectionSet.TryGetSelection("unknown"u8, ref position, out var selection);

        // assert
        Assert.False(found);
        Assert.Null(selection);
        Assert.Equal(expectedPosition, position);
    }

    [Theory]
    [InlineData(SmallOperation)]
    [InlineData(WideOperation)]
    public void TryGetSelection_Should_ResolveEverySelection_When_PositionIsCarriedForward(string operation)
    {
        // arrange
        var selectionSet = CreateRootSelectionSet(operation);
        var resolved = new List<string>();
        var position = 0;

        // act
        foreach (var expected in selectionSet.Selections)
        {
            selectionSet.TryGetSelection(expected.Utf8ResponseName, ref position, out var selection);
            resolved.Add($"{selection?.ResponseName}:{position}");
        }

        // assert
        Assert.Equal(
            selectionSet.Selections.ToArray().Select((selection, i) => $"{selection.ResponseName}:{i + 1}"),
            resolved);
    }

    private static SelectionSet CreateRootSelectionSet(string operation)
    {
        var schema = ComposeSchema(
            """
            # name: test
            type Query {
              a: String
              b: String
              c: String
              d: String
              e: String
              f: String
              g: String
              h: String
              i: String
              j: String
            }
            """);

        return PlanOperation(schema, operation).Operation.RootSelectionSet;
    }
}
