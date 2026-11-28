using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Transport.Http;

namespace HotChocolate.Fusion.Transport;

public class JsonLinesReaderShapeTests
{
    private const string User = """{"user":{"id":1,"name":"a","reviews":[{"body":"x","stars":5}]}}""";
    private const string Product = """{"product":{"upc":"1","price":2}}""";

    // The identified nodes of the user shape, a product is the only one of the product shape.
    private const int ReviewNode = 0;
    private const int UserNode = 1;

    private static readonly SourceResultShape s_userShape = CreateUserShape();
    private static readonly SourceResultShape s_productShape = CreateProductShape();

    [Fact]
    public async Task ReadAsResultStream_Should_IdentifyObjectsOfEveryDocument_When_OneShapeIsExpected()
    {
        // arrange
        using var response = CreateResponse(
            $$"""{"variableIndex":1,"data":{{User}}}""",
            $$"""{"data":{{User}},"variableIndex":0}""",
            $$"""{"variableIndex":2,"data":{{Product}}}""");
        using var arena = new MemoryArena();
        var shapes = new SourceResultShapeSelector(s_userShape);
        string[] expected = ["- - user review", "- - user review", "- - -"];

        // act
        var shapeIds = await ReadShapeIdsAsync(response, arena, shapes);

        // assert
        Assert.Equal(expected, shapeIds);
    }

    [Fact]
    public async Task ReadAsResultStream_Should_IdentifyObjectsByRequestIndex_When_ShapesAreGivenByPosition()
    {
        // arrange
        // the last two documents do not start with a request index that has a shape
        using var response = CreateResponse(
            $$"""{"requestIndex":1,"data":{{Product}}}""",
            $$"""{"requestIndex":0,"variableIndex":1,"data":{{User}}}""",
            $$"""{ "requestIndex" : 0, "variableIndex" : 0, "data" : {{User}} }""",
            $$"""{"requestIndex":1,"data":{{User}}}""",
            $$"""{"data":{{User}},"requestIndex":0}""",
            $$"""{"requestIndex":2,"data":{{User}}}""");
        using var arena = new MemoryArena();
        var shapes = new SourceResultShapeSelector([s_userShape, s_productShape], count: 2);
        string[] expected =
            ["- - product", "- - user review", "- - user review", "- - - -", "- - - -", "- - - -"];

        // act
        var shapeIds = await ReadShapeIdsAsync(response, arena, shapes);

        // assert
        Assert.Equal(expected, shapeIds);
    }

    [Fact]
    public async Task ReadAsResultStream_Should_IdentifyObjects_When_DocumentSpansSeveralChunks()
    {
        // arrange
        // a document of this size is not read as a single span
        var reviews = string.Join(',', Enumerable.Repeat("""{"body":"a review with a body","stars":5}""", 800));
        var user = """{"user":{"id":1,"name":"a","reviews":[""" + reviews + "]}}";
        using var response = CreateResponse($$"""{"requestIndex":0,"data":{{user}}}""");
        using var arena = new MemoryArena();
        var shapes = new SourceResultShapeSelector([s_userShape], count: 1);
        string[] expected = ["- - user" + string.Concat(Enumerable.Repeat(" review", 800))];

        // act
        var shapeIds = await ReadShapeIdsAsync(response, arena, shapes);

        // assert
        Assert.Equal(expected, shapeIds);
    }

    [Fact]
    public async Task ReadAsResultStream_Should_NotIdentifyObjects_When_NoShapeIsExpected()
    {
        // arrange
        using var response = CreateResponse(
            $$"""{"requestIndex":0,"data":{{User}}}""",
            $$"""{"variableIndex":0,"data":{{User}}}""");
        using var arena = new MemoryArena();
        string[] expected = ["- - - -", "- - - -"];

        // act
        var shapeIds = await ReadShapeIdsAsync(response, arena, shapes: default);

        // assert
        Assert.Equal(expected, shapeIds);
    }

    /// <summary>
    /// Reads the documents of <paramref name="response"/> and returns for every document what
    /// each of its objects is identified as, in document order: <c>user</c>, <c>review</c>,
    /// <c>product</c> or <c>-</c> for an object that is not identified.
    /// </summary>
    private static async Task<List<string>> ReadShapeIdsAsync(
        GraphQLHttpResponse response,
        MemoryArena arena,
        SourceResultShapeSelector shapes)
    {
        var documents = response.ReadAsResultStreamAsync(
            new FixedMemoryArenaSource(arena),
            requireStreaming: true,
            Timeout.InfiniteTimeSpan,
            shapes);
        var shapeIds = new List<string>();

        await foreach (var document in documents.WithCancellation(TestContext.Current.CancellationToken))
        {
            using (document)
            {
                shapeIds.Add(GetShapeIds(document));
            }
        }

        arena.Seal();
        return shapeIds;
    }

    private static string GetShapeIds(SourceResultDocument document)
    {
        var shapeIds = new List<string>();
        var cursor = SourceResultDocument.Cursor.Zero;
        var rowCount = document.GetDbRow(cursor).NumberOfRows;

        for (var i = 0; i < rowCount; i++, cursor++)
        {
            var row = document.GetDbRow(cursor);

            if (row.TokenType is JsonTokenType.StartObject)
            {
                shapeIds.Add(
                    document.GetObjectShapeNode(cursor, row) switch
                    {
                        SourceResultShape.NoNode => "-",
                        _ when ReferenceEquals(document.Shape, s_productShape) => "product",
                        UserNode => "user",
                        ReviewNode => "review",
                        var node => node.ToString()
                    });
            }
        }

        return string.Join(' ', shapeIds);
    }

    private static SourceResultShape CreateUserShape()
    {
        var builder = new SourceResultShape.Builder();
        var review = builder.AddObject(
            ["body", "stars"],
            [SourceResultShape.NoNode, SourceResultShape.NoNode],
            identify: true);
        var user = builder.AddObject(
            ["id", "name", "reviews"],
            [SourceResultShape.NoNode, SourceResultShape.NoNode, review],
            identify: true);
        var data = builder.AddObject(["user"], [user], identify: false);
        return builder.Build(builder.AddObject(["data"], [data], identify: false));
    }

    private static SourceResultShape CreateProductShape()
    {
        var builder = new SourceResultShape.Builder();
        var product = builder.AddObject(
            ["upc", "price"],
            [SourceResultShape.NoNode, SourceResultShape.NoNode],
            identify: true);
        var data = builder.AddObject(["product"], [product], identify: false);
        return builder.Build(builder.AddObject(["data"], [data], identify: false));
    }

    private static GraphQLHttpResponse CreateResponse(params string[] lines)
    {
        var body = Encoding.UTF8.GetBytes(string.Join('\n', lines) + '\n');
        var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/jsonl")
        {
            CharSet = "utf-8"
        };
        return new GraphQLHttpResponse(message);
    }
}
