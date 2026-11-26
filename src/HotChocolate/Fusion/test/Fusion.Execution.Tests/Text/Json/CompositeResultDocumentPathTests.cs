using System.Text;
using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Text.Json;

public class CompositeResultDocumentPathTests : FusionTestBase
{
    private static readonly FusionSchemaDefinition s_schema = ComposeSchema(
        """
        type Query { node: Node }
        type Node { child: Node children: [Node] name: String }
        """);

    [Theory]
    [InlineData(17, 2, 63)]
    [InlineData(19, 1, 64)]
    [InlineData(16, 3, 65)]
    [InlineData(18, 2, 66)]
    [InlineData(45, 3, 152)]
    public void CreatePath_Should_ReturnFullPath_When_PathHasManyRows(int objects, int lists, int expectedRows)
    {
        // arrange
        var query = new StringBuilder("{ node { ");
        for (var i = 0; i < objects; i++)
        {
            query.Append("child { ");
        }

        for (var i = 0; i < lists; i++)
        {
            query.Append("children { ");
        }

        query.Append("name");
        for (var i = 0; i < objects + lists; i++)
        {
            query.Append(" }");
        }

        query.Append(" } }");

        var plan = PlanOperation(s_schema, query.ToString());
        var operation = plan.Operation;
        var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), operation, 0);
        var element = document.Data.GetProperty("node");
        element.SetObjectValue(operation.GetSelectionSet(element.AssertSelection()));
        var expected = Path.Root.Append("node");

        for (var i = 0; i < objects; i++)
        {
            element = element.GetProperty("child");
            element.SetObjectValue(operation.GetSelectionSet(element.AssertSelection()));
            expected = expected.Append("child");
        }

        for (var i = 0; i < lists; i++)
        {
            var list = element.GetProperty("children");
            var selection = list.AssertSelection();
            var enumerator = list.InitializeArrayValue(2);
            enumerator.MoveNext();
            enumerator.MoveNext();
            element = enumerator.Current;
            element.SetObjectValue(operation.GetSelectionSet(selection));
            expected = expected.Append("children").Append(1);
        }

        // act
        var rows = CountRows(document, element.Cursor);
        var path = element.Path;

        // assert
        Assert.Equal(expectedRows, rows);
        Assert.Equal(expected.ToString(), path.ToString());
    }

    private static int CountRows(CompositeResultDocument document, CompositeResultDocument.Cursor cursor)
    {
        var rows = 1;
        var parent = new CompositeResultDocument.Cursor(document._metaDb.Get(cursor).Parent);

        while (!parent.IsZero)
        {
            rows++;
            parent = new CompositeResultDocument.Cursor(document._metaDb.Get(parent).Parent);
        }

        return rows;
    }
}
