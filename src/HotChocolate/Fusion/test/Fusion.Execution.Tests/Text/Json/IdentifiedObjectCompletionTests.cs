using System.Text;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Text.Json;

public sealed class IdentifiedObjectCompletionTests : FusionTestBase
{
    private const string Operation = "{ item { a b c { x y } d { x y } kind n f e } }";

    private static readonly FusionSchemaDefinition s_schema = ComposeSchema(
        """
        # name: test
        type Query {
          item: Item
        }
        type Item {
          a: String
          b: Int
          c: Child
          d: [Child]
          kind: Kind
          n: String
          f: Boolean
          e: String
        }
        type Child {
          x: String
          y: Int
        }
        enum Kind {
          SMALL
          LARGE
        }
        """);

    [Fact]
    public void MoveNext_Should_AssignScalarsAndStopAtOtherValues_When_TheObjectIsIdentified()
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var sourceArena = new MemoryArena();
        using var source = Parse(
            sourceArena,
            Operation,
            """
            {"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2}],"kind":"SMALL","n":null,"f":true,"e":"last"}}}
            """);
        using var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), plan.Operation, 0);
        var item = document.Data.GetProperty("item");
        item.SetObjectValue(plan.Operation.GetSelectionSet(item.AssertSelection()), out var target);
        var sourceItem = source.Root.GetProperty("data").GetProperty("item").CreateSnapshot();
        var stops = new List<string>();

        // act
        var created = TryCreate(sourceItem, target, out var completion);

        while (created && completion.MoveNext())
        {
            stops.Add($"{completion.Selection.ResponseName}: {completion.Value.GetRawText()}");
        }

        // assert
        Assert.True(created);
        Assert.Equal(
            ["""c: {"x":"c","y":1}""", """d: [{"x":"d1","y":2}]""", """kind: "SMALL" """.Trim(), "n: null"],
            stops.ToArray());
        Render(document).MatchInlineSnapshot(
            """
            {"item":{"a":"first","b":5,"c":null,"d":null,"kind":null,"n":null,"f":true,"e":"last"}}
            """);
    }

    [Theory]
    [InlineData(
        "{ item { a b c { x y } d { x y } kind n f e } }",
        """{"data":{"item":{"b":5,"a":"first","c":null,"d":null,"kind":null,"n":null,"f":true,"e":"last"}}}""")]
    [InlineData(
        "{ item { a b c { x y } d { x y } kind n f e } }",
        """{"data":{"item":{"a":"first","b":5,"c":null,"d":null,"kind":null,"n":null,"f":true}}}""")]
    [InlineData(
        "{ item { a b c { x y } d { x y } kind n f e } }",
        """{"data":{"item":{"a":"first","b":5,"c":null,"d":null,"kind":null,"n":null,"f":true,"e":"last","g":1}}}""")]
    [InlineData("{ item { a b c { x y } d { x y } kind n f e } }", """{"data":{"item":{}}}""")]
    [InlineData(
        null,
        """{"data":{"item":{"a":"first","b":5,"c":null,"d":null,"kind":null,"n":null,"f":true,"e":"last"}}}""")]
    public void GetNodeSelections_Should_ReturnNull_When_TheSourceObjectIsNotIdentified(
        string? shapeOperation,
        string payload)
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var sourceArena = new MemoryArena();
        using var source = Parse(sourceArena, shapeOperation, payload);
        using var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), plan.Operation, 0);
        var item = document.Data.GetProperty("item");
        item.SetObjectValue(plan.Operation.GetSelectionSet(item.AssertSelection()), out var target);
        var sourceItem = source.Root.GetProperty("data").GetProperty("item").CreateSnapshot();

        // act
        var selections = sourceItem.GetNodeSelections(target.SelectionSet!);

        // assert
        Assert.Null(selections);
    }

    [Fact]
    public void TryCreate_Should_ReturnFalse_When_TheSourceObjectSpansAChunkBoundary()
    {
        // arrange
        // the rows of the list items fill the first chunks of the source document, so some items
        // have rows in two chunks
        const int length = 64;
        var plan = PlanOperation(s_schema, Operation);
        using var sourceArena = new MemoryArena();
        using var source = Parse(sourceArena, Operation, CreateListPayload(length));
        using var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), plan.Operation, 0);
        var targets = CreateListObjects(document, plan.Operation, length);
        var expected = new List<bool>();
        var created = new List<bool>();
        var index = 0;

        // act
        foreach (var sourceItem in source.Root.GetProperty("data").GetProperty("item").GetProperty("d").EnumerateArray())
        {
            var snapshot = sourceItem.CreateSnapshot();
            var last = sourceItem._cursor + (snapshot.GetValueRow().NumberOfRows - 1);
            var target = targets[index++];

            // a result object with rows in two chunks is not part of this test
            if (!IsInOneChunk(document, target))
            {
                continue;
            }

            expected.Add(sourceItem._cursor.Chunk == last.Chunk);
            created.Add(TryCreate(snapshot, target.Context, out _));
        }

        // assert
        Assert.Equal(expected.ToArray(), created.ToArray());
        Assert.Contains(false, expected);
        Assert.Contains(true, expected);
    }

    [Fact]
    public void TryCreate_Should_ReturnFalse_When_TheResultObjectSpansAChunkBoundary()
    {
        // arrange
        // the rows of the result objects fill the first chunks of the result document, so some
        // objects have rows in two chunks
        const int length = 62;
        var plan = PlanOperation(s_schema, Operation);
        using var sourceArena = new MemoryArena();
        using var source = Parse(sourceArena, Operation, CreateListPayload(1));
        using var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), plan.Operation, 0);
        var targets = CreateListObjects(document, plan.Operation, length);
        var sourceItem = source.Root.GetProperty("data").GetProperty("item").GetProperty("d")[0].CreateSnapshot();
        var expected = new List<bool>();
        var created = new List<bool>();

        // act
        foreach (var target in targets)
        {
            expected.Add(IsInOneChunk(document, target));
            created.Add(TryCreate(sourceItem, target.Context, out _));
        }

        // assert
        Assert.Equal(expected.ToArray(), created.ToArray());
        Assert.Contains(false, expected);
        Assert.Contains(true, expected);
    }

    [Fact]
    public void MoveNext_Should_ChangeTheLayoutVersion_When_AScalarReplacesAnObject()
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var sourceArena = new MemoryArena();
        using var source = Parse(
            sourceArena,
            Operation,
            """
            {"data":{"item":{"a":"first","b":5,"c":"scalar","d":null,"kind":null,"n":null,"f":true,"e":"last"}}}
            """);
        using var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), plan.Operation, 0);
        var item = document.Data.GetProperty("item");
        item.SetObjectValue(plan.Operation.GetSelectionSet(item.AssertSelection()), out var target);
        var child = item.GetProperty("c");
        child.SetObjectValue(plan.Operation.GetSelectionSet(child.AssertSelection()));
        var sourceItem = source.Root.GetProperty("data").GetProperty("item").CreateSnapshot();
        var layoutVersion = document.LayoutVersion;
        var shapeVersion = document.ShapeVersion;

        // act
        var created = TryCreate(sourceItem, target, out var completion);

        while (created && completion.MoveNext())
        {
        }

        // assert
        Assert.True(created);
        Assert.Equal(layoutVersion + 1, document.LayoutVersion);
        Assert.Equal(shapeVersion + 1, document.ShapeVersion);
        Render(document).MatchInlineSnapshot(
            """
            {"item":{"a":"first","b":5,"c":"scalar","d":null,"kind":null,"n":null,"f":true,"e":"last"}}
            """);
    }

    [Theory]
    [InlineData("""{"data":{"item":{"a":null,"b":null,"c":null,"d":null,"kind":"SMALL","n":null,"f":null,"e":null}}}""", -1)]
    [InlineData("""{"data":{"item":{"a":null,"b":null,"c":null,"d":null,"kind":"SMALL","n":null,"f":null,"e":"last"}}}""", 0)]
    public void MoveNext_Should_RegisterTheSourceDocument_When_AScalarIsAssigned(string payload, int expectedId)
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var sourceArena = new MemoryArena();
        using var source = Parse(sourceArena, Operation, payload);
        using var document = new CompositeResultDocument(CommonTestExtensions.CreateArena(), plan.Operation, 0);
        var item = document.Data.GetProperty("item");
        item.SetObjectValue(plan.Operation.GetSelectionSet(item.AssertSelection()), out var target);
        var sourceItem = source.Root.GetProperty("data").GetProperty("item").CreateSnapshot();

        // act
        var created = TryCreate(sourceItem, target, out var completion);

        while (created && completion.MoveNext())
        {
        }

        // assert
        Assert.True(created);
        Assert.Equal(expectedId, source.Id);
    }

    /// <summary>
    /// Creates the completion of <paramref name="source"/> into <paramref name="target"/> the
    /// way value completion does, from the selections of the shape node of the object.
    /// </summary>
    private static bool TryCreate(
        SourceResultElementSnapshot source,
        CompositeObjectContext target,
        out IdentifiedObjectCompletion completion)
    {
        if (source.GetNodeSelections(target.SelectionSet!) is { } selections)
        {
            return IdentifiedObjectCompletion.TryCreate(in source, in target, selections, out completion);
        }

        completion = default;
        return false;
    }

    /// <summary>
    /// Creates the object <c>item</c> with a list <c>d</c> of <paramref name="length"/> objects
    /// in <paramref name="document"/> and returns the objects of the list.
    /// </summary>
    private static List<(CompositeResultElement Element, CompositeObjectContext Context)> CreateListObjects(
        CompositeResultDocument document,
        Operation operation,
        int length)
    {
        var item = document.Data.GetProperty("item");
        item.SetObjectValue(operation.GetSelectionSet(item.AssertSelection()));

        var list = item.GetProperty("d");
        var selectionSet = operation.GetSelectionSet(list.AssertSelection());
        list.SetArrayValue(length);

        var objects = new List<(CompositeResultElement, CompositeObjectContext)>();

        foreach (var element in list.EnumerateArray())
        {
            element.SetObjectValue(selectionSet, out var context);
            objects.Add((element, context));
        }

        return objects;
    }

    private static bool IsInOneChunk(
        CompositeResultDocument document,
        (CompositeResultElement Element, CompositeObjectContext Context) target)
    {
        var start = document.GetStartCursor(target.Element.Cursor);
        var lastValue = document.GetEndCursor(start) - 1;

        return start.Chunk == lastValue.Chunk;
    }

    private static string CreateListPayload(int length)
    {
        var items = string.Join(",", Enumerable.Range(0, length).Select(i => $$"""{"x":"d{{i}}","y":{{i}} }"""));

        return $$"""{"data":{"item":{"a":"first","b":5,"c":null,"d":[{{items}}],"kind":null,"n":null,"f":true,"e":"last"} } }""";
    }

    /// <summary>
    /// Parses <paramref name="payloadJson"/> with the expected shape of the results of
    /// <paramref name="shapeOperation"/>, or without a shape when no operation is given.
    /// </summary>
    private static SourceResultDocument Parse(MemoryArena arena, string? shapeOperation, string payloadJson)
    {
        var shape = shapeOperation is null
            ? null
            : OperationResultShape.Create(
                Utf8GraphQLOperationParser.Parse(Encoding.UTF8.GetBytes(shapeOperation), ParserOptions.Trusted)).Single;

        using var writer = new ArenaBufferWriter(arena);
        var payload = Encoding.UTF8.GetBytes(payloadJson.Trim());

        payload.CopyTo(writer.GetSpan(payload.Length));
        writer.Advance(payload.Length);

        return SourceResultDocument.ParseFilled(arena, writer.Segments, writer.UsedChunks, writer.LastLength, shape);
    }

    private static string Render(CompositeResultDocument document)
    {
        using var buffer = new PooledArrayWriter();
        document.Data.WriteTo(buffer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
