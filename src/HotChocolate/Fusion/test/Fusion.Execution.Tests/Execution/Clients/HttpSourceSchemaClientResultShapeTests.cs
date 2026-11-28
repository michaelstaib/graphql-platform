using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Transport.Http;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Clients;

public sealed class HttpSourceSchemaClientResultShapeTests : SourceSchemaClientTestBase
{
    private const string UserOperation = "query { user { id name } }";
    private const string ProductOperation = "query { product { upc } }";
    private const string UserData = """{"user":{"id":1,"name":"a"}}""";
    private const string ProductData = """{"product":{"upc":"1"}}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_Should_ExpectTheShapeOfTheRequest_When_VariableBatchIsAnsweredWithJsonLines(
        bool buffered)
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        await using var client = CreateClient(
            SourceSchemaClientCapabilities.Default,
            $$"""{"variableIndex":1,"data":{{UserData}}}""",
            $$"""{"data":{{UserData}},"variableIndex":0}""");
        var context = fixture.CreateContext();
        var requests = ImmutableArray.Create(CreateRequest(fixture.RootNode, UserOperation, variableCount: 2));

        // act
        var results = await ExecuteAsync(client, context, requests, buffered, batch: false);

        // assert
        results.MatchInlineSnapshots(
        [
            """0 [1] shape of request 0, 2 objects identified, {"user":{"id":1,"name":"a"}}""",
            """0 [0] shape of request 0, 2 objects identified, {"user":{"id":1,"name":"a"}}"""
        ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteBatch_Should_ExpectTheShapeOfEachRequest_When_ResultsStartWithRequestIndex(
        bool buffered)
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        await using var client = CreateClient(
            SourceSchemaClientCapabilities.Default,
            $$"""{"requestIndex":1,"data":{{ProductData}}}""",
            $$"""{"requestIndex":0,"data":{{UserData}}}""");
        var context = fixture.CreateContext();
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, UserOperation, variableCount: 0),
            CreateRequest(fixture.RootNode, ProductOperation, variableCount: 0));

        // act
        var results = await ExecuteAsync(client, context, requests, buffered, batch: true);

        // assert
        results.MatchInlineSnapshots(
        [
            """1 root shape of request 1, 2 objects identified, {"product":{"upc":"1"}}""",
            """0 root shape of request 0, 2 objects identified, {"user":{"id":1,"name":"a"}}"""
        ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteBatch_Should_ExpectTheShapeOfEachRequest_When_EveryVariableSetIsItsOwnOperation(
        bool buffered)
    {
        // arrange
        // without variable batching the first two operations of the batch belong to the first request
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        await using var client = CreateClient(
            SourceSchemaClientCapabilities.RequestBatching,
            $$"""{"requestIndex":2,"data":{{ProductData}}}""",
            $$"""{"requestIndex":0,"data":{{UserData}}}""",
            $$"""{"requestIndex":1,"data":{{UserData}}}""");
        var context = fixture.CreateContext();
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, UserOperation, variableCount: 2),
            CreateRequest(fixture.RootNode, ProductOperation, variableCount: 1));

        // act
        var results = await ExecuteAsync(client, context, requests, buffered, batch: true);

        // assert
        results.MatchInlineSnapshots(
        [
            """1 [0] shape of request 1, 2 objects identified, {"product":{"upc":"1"}}""",
            """0 [0] shape of request 0, 2 objects identified, {"user":{"id":1,"name":"a"}}""",
            """0 [1] shape of request 0, 2 objects identified, {"user":{"id":1,"name":"a"}}"""
        ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteBatch_Should_ReturnTheData_When_ResultDoesNotHaveTheExpectedShape(bool buffered)
    {
        // arrange
        // the first result answers the first request with the data of the second, and the second
        // result names its request after its data
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        await using var client = CreateClient(
            SourceSchemaClientCapabilities.Default,
            $$"""{"requestIndex":0,"data":{{ProductData}}}""",
            $$"""{"data":{{ProductData}},"requestIndex":1}""");
        var context = fixture.CreateContext();
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, UserOperation, variableCount: 0),
            CreateRequest(fixture.RootNode, ProductOperation, variableCount: 0));

        // act
        var results = await ExecuteAsync(client, context, requests, buffered, batch: true);

        // assert
        results.MatchInlineSnapshots(
        [
            """0 root shape of request 0, 0 objects identified, {"product":{"upc":"1"}}""",
            """1 root no shape, 0 objects identified, {"product":{"upc":"1"}}"""
        ]);
    }

    private static HttpSourceSchemaClient CreateClient(
        SourceSchemaClientCapabilities capabilities,
        params string[] documents)
    {
        var content = Encoding.UTF8.GetBytes(string.Join('\n', documents) + '\n');
        var graphQLClient = new DefaultGraphQLHttpClient(
            new HttpClient(new JsonLinesHandler(content)),
            disposeInnerClient: true);

        return new HttpSourceSchemaClient(
            graphQLClient,
            new HttpSourceSchemaClientConfiguration(
                "A",
                new Uri("http://localhost:5000/graphql"),
                capabilities: capabilities));
    }

    private static SourceSchemaClientRequest CreateRequest(ExecutionNode node, string operation, int variableCount)
    {
        var sourceText = Encoding.UTF8.GetBytes(operation);
        var document = Utf8GraphQLOperationParser.Parse(sourceText);
        var variables = ImmutableArray.CreateBuilder<VariableValues>(variableCount);

        for (var i = 0; i < variableCount; i++)
        {
            variables.Add(new VariableValues(new CompactPath([1, ~i]), JsonSegment.Empty));
        }

        return new SourceSchemaClientRequest
        {
            Node = node,
            SchemaName = "A",
            OperationType = OperationType.Query,
            OperationSourceText = new OperationSourceText(
                "Op",
                OperationType.Query,
                sourceText,
                OperationSourceTextHash.Compute(sourceText)),
            OperationDocument = document,
            Variables = variables.MoveToImmutable(),
            ResultShape = OperationResultShape.Create(document)
        };
    }

    /// <summary>
    /// Executes the requests and returns for every result its request, its path, the request
    /// whose shape was expected of its document, the number of objects that were identified and
    /// its data.
    /// </summary>
    private static async Task<List<string>> ExecuteAsync(
        HttpSourceSchemaClient client,
        OperationPlanContext context,
        ImmutableArray<SourceSchemaClientRequest> requests,
        bool buffered,
        bool batch)
    {
        var sink = new RecordingSink(requests);
        var cancellationToken = TestContext.Current.CancellationToken;

        if (batch && buffered)
        {
            await client.ExecuteBatchBufferedAsync(context, requests, sink, cancellationToken);
        }
        else if (batch)
        {
            await foreach (var result in client.ExecuteBatchAsync(context, requests, cancellationToken))
            {
                sink.OnResult(result.RequestIndex, result.Result);
            }
        }
        else if (buffered)
        {
            await client.ExecuteBufferedAsync(context, requests[0], sink, cancellationToken);
        }
        else
        {
            await foreach (var result in client.ExecuteAsync(context, requests[0], cancellationToken))
            {
                sink.OnResult(0, result);
            }
        }

        return sink.Lines;
    }

    private sealed class RecordingSink(ImmutableArray<SourceSchemaClientRequest> requests) : ISourceSchemaResultSink
    {
        public List<string> Lines { get; } = [];

        public bool OnResult(int requestIndex, SourceSchemaResult result)
        {
            using (result)
            {
                var document = result.Data._parent;
                Lines.Add(
                    $"{requestIndex} {FormatPath(result.Path)} {FormatShape(document)}, "
                    + $"{CountIdentifiedObjects(document)} objects identified, {result.Data.GetRawText()}");
            }

            return true;
        }

        private string FormatShape(SourceResultDocument document)
        {
            for (var i = 0; i < requests.Length; i++)
            {
                if (ReferenceEquals(document.Shape, requests[i].ResultShape!.Single))
                {
                    return $"shape of request {i}";
                }
            }

            return document.Shape is null ? "no shape" : "another shape";
        }

        private static int CountIdentifiedObjects(SourceResultDocument document)
        {
            var count = 0;
            var cursor = SourceResultDocument.Cursor.Zero;
            var rowCount = document.GetDbRow(cursor).NumberOfRows;

            for (var i = 0; i < rowCount; i++, cursor++)
            {
                var row = document.GetDbRow(cursor);

                if (row.TokenType is JsonTokenType.StartObject && document.GetObjectShapeNode(cursor, row) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FormatPath(CompactPath path)
        {
            if (path.IsRoot)
            {
                return "root";
            }

            var builder = new StringBuilder();

            foreach (var segment in path.Segments)
            {
                builder.Append('[').Append(~segment).Append(']');
            }

            return builder.ToString();
        }
    }

    private sealed class JsonLinesHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = new ByteArrayContent(content);
            body.Headers.TryAddWithoutValidation("Content-Type", "application/jsonl; charset=utf-8");

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = body });
        }
    }
}
