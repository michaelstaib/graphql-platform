using System.Text;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Execution.Errors;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Results;

public sealed class FetchResultStoreShapeTests : FusionTestBase
{
    private const string Operation = "{ item { a b c { x y } d { x y } e } }";

    private static readonly FusionSchemaDefinition s_schema = ComposeSchema(
        """
        # name: test
        type Query {
          item: Item
          pet: Pet
          strict: Strict
          stricts: [Strict]
        }
        type Item {
          a: String
          b: Int
          c: Child
          d: [Child]
          e: String
          kind: Kind
          kinds: [Kind]
          grid: [[Child]]
        }
        type Child {
          x: String
          y: Int
        }
        type Strict {
          id: String!
          name: String
        }
        enum Kind {
          SMALL
          LARGE
        }
        interface Pet {
          name: String
        }
        type Dog implements Pet {
          name: String
          barks: Boolean
        }
        type Cat implements Pet {
          name: String
          lives: Int
        }
        """);

    [Theory]
    [InlineData(
        """
        {"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2},{"x":"d2","y":3}],"e":"last"}}}
        """)]
    [InlineData(
        """
        {"data":{"item":{"e":"last","d":[{"y":2,"x":"d1"},{"x":"d2","y":3}],"c":{"y":1,"x":"c"},"b":5,"a":"first"}}}
        """)]
    [InlineData(
        """
        {"data":{"item":{"c":{"x":"c","y":1},"unknown":true,"a":"first","e":"last","d":[{"x":"d1","y":2},{"y":3,"x":"d2"}],"b":5}}}
        """)]
    [InlineData(
        """
        { "data" : { "item" : { "a" : "first", "b" : 5, "c" : { "x" : "c", "y" : 1 }, "d" : [ { "x" : "d1", "y" : 2 }, { "x" : "d2", "y" : 3 } ], "e" : "last" } } }
        """)]
    public void AddPartialResults_Should_CompleteEveryField_When_TheResultIsParsedWithItsExpectedShape(string payload)
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();

        // act
        using var store = CreateStore(plan, GetShape(plan), payload, resultArena, sourceArena);

        // assert
        RenderData(store).MatchInlineSnapshot(
            """
            {"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2},{"x":"d2","y":3}],"e":"last"}}
            """);
        Assert.Null(store.Errors);
    }

    [Theory]
    [InlineData(Operation, """{"data":{"item":{"a":null,"b":5,"c":null,"d":[null,{"x":"d2","y":null}],"e":"last"}}}""")]
    [InlineData(Operation, """{"data":{"item":{"a":"first","b":5,"c":{"x":"c"},"d":[],"e":"last"}}}""")]
    [InlineData(Operation, """{"data":{"item":{"a":"first","b":5}}}""")]
    [InlineData(Operation, """{"data":{"item":null}}""")]
    [InlineData(
        Operation,
        """{"data":{"item":{"a":"first","b":5,"c":{"y":1,"x":"c","x":"again"},"d":[{"x":"d1","y":2}],"e":"last"}}}""")]
    [InlineData(
        Operation,
        """{"errors":[{"message":"boom","path":["item","c","x"]}],"data":{"item":{"a":"first","b":5,"c":{"x":null,"y":1},"d":[],"e":"last"}}}""")]
    [InlineData(
        Operation,
        """{"data":{"item":{"a":"first","b":5,"c":null,"d":[{"x":"d1","y":2}],"e":"last"}},"errors":[{"message":"boom","path":["item","c"]},{"message":"root"}]}""")]
    [InlineData(
        "{ pet { name ... on Dog { barks } ... on Cat { lives } } }",
        """{"data":{"pet":{"__typename":"Dog","name":"Rex","barks":true}}}""")]
    [InlineData(
        "{ pet { name ... on Dog { barks } ... on Cat { lives } } }",
        """{"data":{"pet":{"name":"Tom","lives":9,"__typename":"Cat"}}}""")]
    [InlineData(
        "{ pet { name } }",
        """{"errors":[{"message":"boom","path":["pet","name"]}],"data":{"pet":{"__typename":"Cat","name":null}}}""")]
    [InlineData(Operation, """{"data":{"item":{"a":"first","b":5,"c":{},"d":[{},{"x":"d2","y":3},{}],"e":"last"}}}""")]
    [InlineData(Operation, """{"data":{"item":{"a":true,"b":"5","c":"c","d":7,"e":1.5}}}""")]
    [InlineData(
        "{ item { a kind kinds e } }",
        """{"data":{"item":{"a":"first","kind":"SMALL","kinds":["LARGE","SMALL"],"e":"last"}}}""")]
    [InlineData(
        "{ item { a kind kinds e } }",
        """{"data":{"item":{"a":"first","kind":"HUGE","kinds":["LARGE","HUGE",null],"e":"last"}}}""")]
    [InlineData(
        "{ item { a kind kinds e } }",
        """{"data":{"item":{"a":"first","kind":1,"kinds":[true,2],"e":"last"}}}""")]
    [InlineData(
        "{ item { a kind kinds e } }",
        """{"data":{"item":{"a":"first","kind":null,"kinds":null,"e":"last"}}}""")]
    [InlineData(
        "{ item { a grid { x y } e } }",
        """{"data":{"item":{"a":"first","grid":[[{"x":"g1","y":1},{"x":"g2","y":2}],[],null,[{"x":"g3","y":null}]],"e":"last"}}}""")]
    [InlineData("{ strict { id name } }", """{"data":{"strict":{"id":"1","name":"one"}}}""")]
    [InlineData("{ strict { id name } }", """{"data":{"strict":{"id":null,"name":"one"}}}""")]
    [InlineData(
        "{ stricts { id name } }",
        """{"data":{"stricts":[{"id":"1","name":"one"},{"id":null,"name":"two"},{"id":"3","name":null}]}}""")]
    [InlineData(
        "{ stricts { id name } }",
        """{"errors":[{"message":"boom","path":["stricts",1,"name"]}],"data":{"stricts":[{"id":"1","name":"one"},{"id":"2","name":null}]}}""")]
    public void AddPartialResults_Should_CreateTheSameResult_When_TheResultIsParsedWithAndWithoutItsExpectedShape(
        string operation,
        string payload)
    {
        // arrange
        var plan = PlanOperation(s_schema, operation);
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();
        using var expectedResultArena = new MemoryArena();
        using var expectedSourceArena = new MemoryArena();
        using var expected = CreateStore(plan, shape: null, payload, expectedResultArena, expectedSourceArena);

        // act
        using var store = CreateStore(plan, GetShape(plan), payload, resultArena, sourceArena);

        // assert
        Assert.Equal(RenderData(expected), RenderData(store));
        Assert.Equal(
            expected.Errors?.Select(t => $"{t.Message} {t.Path}").ToArray(),
            store.Errors?.Select(t => $"{t.Message} {t.Path}").ToArray());
    }

    [Fact]
    public void AddPartialResults_Should_SkipUnselectedSourceFields_When_TheResultHasItsExpectedShape()
    {
        // arrange
        // the source schema operation selects more fields than the operation of the client
        var plan = PlanOperation(s_schema, "{ item { e c { y } } }");
        var shape = OperationResultShape.Create(ParseOperation(Operation)).Single;
        const string payload =
            """
            {"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2}],"e":"last"}}}
            """;
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();

        // act
        using var store = CreateStore(plan, shape, payload, resultArena, sourceArena);

        // assert
        RenderData(store).MatchInlineSnapshot(
            """
            {"item":{"e":"last","c":{"y":1}}}
            """);
        Assert.Null(store.Errors);
    }

    [Fact]
    public void AddPartialResults_Should_CompleteByName_When_TheShapeKeepsSelectionsForEightOtherSelectionSets()
    {
        // arrange
        const string payload =
            """
            {"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2}],"e":"last"}}}
            """;
        var shape = OperationResultShape.Create(ParseOperation(Operation)).Single;
        var completed = new List<string>();

        // act
        // every plan has its own selection sets, the ninth plan finds the lists of the shape full
        for (var i = 0; i < 9; i++)
        {
            using var resultArena = new MemoryArena();
            using var sourceArena = new MemoryArena();
            using var store = CreateStore(PlanOperation(s_schema, Operation), shape, payload, resultArena, sourceArena);

            completed.Add(RenderData(store));
        }

        // assert
        Assert.Equal(
            Enumerable
                .Repeat("""{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2}],"e":"last"}}""", 9)
                .ToArray(),
            completed.ToArray());
    }

    [Theory]
    [InlineData("""{"data":{"item":{"a":"first","b":5,"c":[],"d":[],"e":"last"}}}""", null)]
    [InlineData("""{"data":{"item":{"a":"first","b":5,"c":null,"d":[7],"e":"last"}}}""", null)]
    [InlineData(
        """{"data":{"item":{"a":"first","b":5,"c":null,"d":null,"e":"last"}}}""",
        """{"data":{"item":{"a":"second","b":6,"c":{"x":"c2","y":7},"d":[{"x":"d3","y":8}],"e":"end"}}}""")]
    public void AddPartialResults_Should_ThrowTheSameException_When_AValueCannotBeCompleted(
        string first,
        string? second)
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        var shape = GetShape(plan);
        string[] payloads = second is null ? [first] : [first, second];

        // act
        var expected = Record.Exception(() => CreateAndRenderStore(plan, shape: null, payloads));
        var exception = Record.Exception(() => CreateAndRenderStore(plan, shape, payloads));

        // assert
        Assert.NotNull(expected);
        Assert.NotNull(exception);
        Assert.Equal(expected.GetType(), exception.GetType());
        Assert.Equal(expected.Message, exception.Message);
    }

    [Fact]
    public void AddPartialResults_Should_CreateTheSameResult_When_ObjectsSpanChunkBoundaries()
    {
        // arrange
        // every list length moves the rows of the list items by one row, so that in both
        // documents some items lie on the boundary of the first chunks and some next to it
        var plan = PlanOperation(s_schema, Operation);
        var shape = GetShape(plan);
        var expected = new List<string>();
        var completed = new List<string>();
        var completedWithoutShape = new List<string>();
        var spanningResultObjects = 0;
        var spanningSourceObjects = 0;
        var identified = true;

        // act
        for (var length = 1; length <= 48; length++)
        {
            var items = string.Join(",", Enumerable.Range(0, length).Select(i => $"{{\"x\":\"d{i}\",\"y\":{i}}}"));
            var data = $"{{\"item\":{{\"a\":\"first\",\"b\":5,\"c\":{{\"x\":\"c\",\"y\":1}},\"d\":[{items}],\"e\":\"last\"}}}}";
            var payload = $"{{\"data\":{data}}}";

            using var resultArena = new MemoryArena();
            using var sourceArena = new MemoryArena();
            using var store = CreateStore(plan, shape, payload, resultArena, sourceArena);

            expected.Add(data);
            completed.Add(RenderData(store));
            completedWithoutShape.Add(CreateAndRenderStore(plan, shape: null, [payload]));
            spanningResultObjects += CountSpanningResultObjects(store);
            spanningSourceObjects += CountSpanningSourceObjects(shape, payload);
            identified &= IsItemIdentified(shape, payload);
        }

        // assert
        Assert.True(identified);
        Assert.Equal(expected.ToArray(), completed.ToArray());
        Assert.Equal(expected.ToArray(), completedWithoutShape.ToArray());
        Assert.True(spanningResultObjects > 0, "No result object spans a chunk boundary.");
        Assert.True(spanningSourceObjects > 0, "No source object spans a chunk boundary.");
    }

    [Theory]
    [InlineData(
        """{"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2},{"x":"d2","y":3}],"e":"last"}}}""",
        """{"data":{"item":{"a":"second","b":6,"c":{"x":"c2","y":7},"d":[{"x":"d3","y":8},{"x":"d4","y":9}],"e":"end"}}}""")]
    [InlineData(
        """{"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2},{"x":"d2","y":3}],"e":"last"}}}""",
        """{"data":{"item":{"a":null,"b":null,"c":{"x":null,"y":7},"d":[null,{"x":"d4","y":null}],"e":null}}}""")]
    [InlineData(
        """{"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2},{"x":"d2","y":3}],"e":"last"}}}""",
        """{"data":{"item":{"a":"second","b":6,"c":"scalar","d":8,"e":"end"}}}""")]
    [InlineData(
        """{"data":{"item":{"a":"first","b":5,"c":"scalar","d":8,"e":"last"}}}""",
        """{"data":{"item":{"a":"second","b":6,"c":"other","d":9,"e":"end"}}}""")]
    public void AddPartialResults_Should_CreateTheSameResult_When_ASecondResultIsCompletedIntoTheSameObjects(
        string first,
        string second)
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();
        using var expectedResultArena = new MemoryArena();
        using var expectedSourceArena = new MemoryArena();
        using var expected = CreateStore(
            plan,
            shape: null,
            [first, second],
            includeFlags: default,
            expectedResultArena,
            expectedSourceArena);

        // act
        using var store = CreateStore(
            plan,
            GetShape(plan),
            [first, second],
            includeFlags: default,
            resultArena,
            sourceArena);

        // assert
        Assert.Equal(RenderData(expected), RenderData(store));
        Assert.Equal(expected.Result.LayoutVersion, store.Result.LayoutVersion);
        Assert.Equal(expected.Result.ShapeVersion, store.Result.ShapeVersion);
        Assert.Equal(
            expected.Errors?.Select(t => $"{t.Message} {t.Path}").ToArray(),
            store.Errors?.Select(t => $"{t.Message} {t.Path}").ToArray());
    }

    [Fact]
    public void AddPartialResults_Should_KeepTheValuesOfTheLastResult_When_TwoResultsAreCompletedIntoTheSameObjects()
    {
        // arrange
        var plan = PlanOperation(s_schema, Operation);
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();

        // act
        using var store = CreateStore(
            plan,
            GetShape(plan),
            [
                """{"data":{"item":{"a":"first","b":5,"c":{"x":"c","y":1},"d":[{"x":"d1","y":2}],"e":"last"}}}""",
                """{"data":{"item":{"a":"second","b":6,"c":{"x":"c2","y":7},"d":[{"x":"d3","y":8}],"e":"end"}}}"""
            ],
            includeFlags: default,
            resultArena,
            sourceArena);

        // assert
        RenderData(store).MatchInlineSnapshot(
            """
            {"item":{"a":"second","b":6,"c":{"x":"c2","y":7},"d":[{"x":"d3","y":8}],"e":"end"}}
            """);
        Assert.Null(store.Errors);
    }

    [Theory]
    [InlineData(0UL, """{"item":{"a":"first","e":"last"}}""")]
    [InlineData(ulong.MaxValue, """{"item":{"a":"first","e":"last","b":5,"c":{"x":"c","y":1}}}""")]
    public void AddPartialResults_Should_CreateTheSameResult_When_ASelectionIsConditional(
        ulong includeFlags,
        string expectedData)
    {
        // arrange
        // the source schema operation selects the conditional fields after the other fields
        const string payload =
            """{"data":{"item":{"a":"first","e":"last","b":5,"c":{"x":"c","y":1}}}}""";
        var plan = PlanOperation(
            s_schema,
            "query($skip: Boolean!, $include: Boolean!) { item { a b @skip(if: $skip) c @include(if: $include) { x y } e } }");
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();
        using var expectedResultArena = new MemoryArena();
        using var expectedSourceArena = new MemoryArena();
        using var expected = CreateStore(
            plan,
            shape: null,
            [payload],
            new ConditionFlags(includeFlags),
            expectedResultArena,
            expectedSourceArena);

        // act
        using var store = CreateStore(
            plan,
            GetShape(plan),
            [payload],
            new ConditionFlags(includeFlags),
            resultArena,
            sourceArena);

        // assert
        Assert.True(IsItemIdentified(GetShape(plan), payload));
        Assert.Equal(expectedData, RenderData(store));
        Assert.Equal(expectedData, RenderData(expected));
        Assert.Null(store.Errors);
    }

    /// <summary>
    /// Gets the expected shape of the results of the single source schema operation of
    /// <paramref name="plan"/>.
    /// </summary>
    private static SourceResultShape GetShape(OperationPlan plan)
    {
        var node = Assert.IsType<OperationExecutionNode>(Assert.Single(plan.RootNodes));

        return OperationResultShape.Create(
            Utf8GraphQLOperationParser.Parse(node.Operation.Value, ParserOptions.Trusted)).Single;
    }

    private static Utf8OperationDocument ParseOperation(string operation)
        => Utf8GraphQLOperationParser.Parse(Encoding.UTF8.GetBytes(operation), ParserOptions.Trusted);

    /// <summary>
    /// Creates a store for <paramref name="plan"/> and adds the result <paramref name="payloadJson"/>,
    /// parsed with <paramref name="shape"/> when one is given.
    /// </summary>
    private static FetchResultStore CreateStore(
        OperationPlan plan,
        SourceResultShape? shape,
        string payloadJson,
        MemoryArena resultArena,
        MemoryArena sourceArena)
        => CreateStore(plan, shape, [payloadJson], includeFlags: default, resultArena, sourceArena);

    /// <summary>
    /// Creates a store for <paramref name="plan"/> and adds the results <paramref name="payloads"/>
    /// one after the other, each parsed with <paramref name="shape"/> when one is given.
    /// </summary>
    private static FetchResultStore CreateStore(
        OperationPlan plan,
        SourceResultShape? shape,
        string[] payloads,
        ConditionFlags includeFlags,
        MemoryArena resultArena,
        MemoryArena sourceArena)
    {
        var node = Assert.IsType<OperationExecutionNode>(Assert.Single(plan.RootNodes));
        var store = new FetchResultStore();
        store.Initialize(
            resultArena,
            s_schema,
            DefaultErrorHandler.Default,
            plan.Operation,
            ErrorHandlingMode.Propagate,
            includeFlags,
            deferFlags: default,
            pathSegmentLocalPoolCapacity: 16);

        foreach (var payload in payloads)
        {
            var result = new SourceSchemaResult(CompactPath.Root, ParseDocument(shape, payload, sourceArena));

            store.AddPartialResults(
                SelectionPath.Root,
                [result],
                node.ResultSelectionSet,
                containsErrors: result.HasErrors);
        }

        return store;
    }

    private static string CreateAndRenderStore(OperationPlan plan, SourceResultShape? shape, string[] payloads)
    {
        using var resultArena = new MemoryArena();
        using var sourceArena = new MemoryArena();
        using var store = CreateStore(plan, shape, payloads, includeFlags: default, resultArena, sourceArena);

        return RenderData(store);
    }

    private static SourceResultDocument ParseDocument(
        SourceResultShape? shape,
        string payloadJson,
        MemoryArena sourceArena)
    {
        using var writer = new ArenaBufferWriter(sourceArena);
        var payload = Encoding.UTF8.GetBytes(payloadJson.Trim());

        payload.CopyTo(writer.GetSpan(payload.Length));
        writer.Advance(payload.Length);

        return SourceResultDocument.ParseFilled(
            sourceArena,
            writer.Segments,
            writer.UsedChunks,
            writer.LastLength,
            shape);
    }

    /// <summary>
    /// Determines whether the object <c>data.item</c> of <paramref name="payload"/> is identified
    /// as an object of a node of <paramref name="shape"/>.
    /// </summary>
    private static bool IsItemIdentified(SourceResultShape shape, string payload)
    {
        using var sourceArena = new MemoryArena();
        using var document = ParseDocument(shape, payload, sourceArena);
        var item = document.Root.GetProperty("data").GetProperty("item");

        return document.GetObjectShapeNode(item._cursor, item.GetValueRow()) >= 0;
    }

    /// <summary>
    /// Counts the objects of the list <c>item.d</c> of the result whose rows lie in two chunks.
    /// </summary>
    private static int CountSpanningResultObjects(FetchResultStore store)
    {
        var count = 0;

        foreach (var item in store.Result.Data.GetProperty("item").GetProperty("d").EnumerateArray())
        {
            var start = store.Result.GetStartCursor(item.Cursor);
            var lastValue = store.Result.GetEndCursor(start) - 1;

            if (start.Chunk != lastValue.Chunk)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts the objects of the list <c>data.item.d</c> of <paramref name="payload"/> whose rows
    /// lie in two chunks.
    /// </summary>
    private static int CountSpanningSourceObjects(SourceResultShape shape, string payload)
    {
        using var sourceArena = new MemoryArena();
        using var document = ParseDocument(shape, payload, sourceArena);
        var count = 0;

        foreach (var item in document.Root.GetProperty("data").GetProperty("item").GetProperty("d").EnumerateArray())
        {
            var end = item._cursor + (item.GetValueRow().NumberOfRows - 1);

            if (item._cursor.Chunk != end.Chunk)
            {
                count++;
            }
        }

        return count;
    }

    private static string RenderData(FetchResultStore store)
    {
        using var buffer = new PooledArrayWriter();
        store.Result.Data.WriteTo(buffer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
