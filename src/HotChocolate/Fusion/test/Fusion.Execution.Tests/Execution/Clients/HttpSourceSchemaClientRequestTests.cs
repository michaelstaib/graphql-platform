using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Text;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Transport.Http;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Clients;

public sealed class HttpSourceSchemaClientRequestTests : SourceSchemaClientTestBase
{
    private const string SingleQuery = "query A { field }";
    private const string VariableQuery = "query B($id: Int!) { field(id: $id) }";
    private const string OtherVariableQuery = "query C($id: Int!) { field(id: $id) }";

    private static readonly Uri s_baseAddress = new("http://localhost:5000/graphql");

    [Fact]
    public async Task ExecuteAsync_Should_SendSingleOperation_When_RequestHasNoVariableSets()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration(
            "A",
            s_baseAddress,
            onError: ErrorHandlingMode.Propagate);
        var request = CreateRequest(fixture.RootNode, SingleQuery);

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => DrainAsync(client.ExecuteAsync(context, request, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/graphql-response+json; charset=utf-8, application/json; charset=utf-8, application/jsonl; charset=utf-8, text/event-stream; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            {"query":"query A { field }","onError":"PROPAGATE"}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_SendSingleOperation_When_RequestHasOneVariableSet()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration("A", s_baseAddress);
        var request = CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}""");

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => DrainAsync(client.ExecuteAsync(context, request, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/graphql-response+json; charset=utf-8, application/json; charset=utf-8, application/jsonl; charset=utf-8, text/event-stream; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            {"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_SendVariableBatch_When_SourceSchemaSupportsVariableBatching()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration("A", s_baseAddress);
        var request = CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}""", """{"id":2}""");

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => DrainAsync(client.ExecuteAsync(context, request, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            {"query":"query B($id: Int!) { field(id: $id) }","variables":[{"id":1},{"id":2}]}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_SendOperationPerVariableSet_When_SourceSchemaLacksVariableBatching()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration(
            "A",
            s_baseAddress,
            capabilities: SourceSchemaClientCapabilities.RequestBatching);
        var request = CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}""", """{"id":2}""");

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => DrainAsync(client.ExecuteAsync(context, request, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            [{"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}},{"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":2}}]
            """);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_SendOperationBatch_When_SourceSchemaSupportsVariableBatching()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration(
            "A",
            s_baseAddress,
            onError: ErrorHandlingMode.Null);
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, SingleQuery),
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}"""),
            CreateRequest(fixture.RootNode, OtherVariableQuery, """{"id":2}""", """{"id":3}"""));

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => DrainAsync(client.ExecuteBatchAsync(context, requests, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            [{"query":"query A { field }","onError":"NULL"},{"query":"query B($id: Int!) { field(id: $id) }","onError":"NULL","variables":{"id":1}},{"query":"query C($id: Int!) { field(id: $id) }","onError":"NULL","variables":[{"id":2},{"id":3}]}]
            """);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_SendOperationPerVariableSet_When_SourceSchemaLacksVariableBatching()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration(
            "A",
            s_baseAddress,
            capabilities: SourceSchemaClientCapabilities.RequestBatching);
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}"""),
            CreateRequest(fixture.RootNode, OtherVariableQuery, """{"id":2}""", """{"id":3}"""));

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => DrainAsync(client.ExecuteBatchAsync(context, requests, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            [{"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}},{"query":"query C($id: Int!) { field(id: $id) }","variables":{"id":2}},{"query":"query C($id: Int!) { field(id: $id) }","variables":{"id":3}}]
            """);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_SendEntryWithoutArray_When_BatchHasOneEntry()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration("A", s_baseAddress);
        var variableBatch = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, OtherVariableQuery, """{"id":2}""", """{"id":3}"""));
        var singleOperation = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}"""));

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            async (client, context) =>
            {
                await DrainAsync(client.ExecuteBatchAsync(context, variableBatch, TestContext.Current.CancellationToken));
                await DrainAsync(client.ExecuteBatchAsync(context, singleOperation, TestContext.Current.CancellationToken));
            });

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        direct.Requests.MatchInlineSnapshots(
            [
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                {"query":"query C($id: Int!) { field(id: $id) }","variables":[{"id":2},{"id":3}]}
                """,
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                {"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}}
                """
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_InvokeRequestHooks_When_HooksAreConfigured()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var hookCalls = new List<string>();
        var configuration = new HttpSourceSchemaClientConfiguration(
            "A",
            s_baseAddress,
            onBeforeSend: (_, node, message) =>
            {
                message.Headers.TryAddWithoutValidation("X-Hook", "before");
                hookCalls.Add($"before {node.Id}");
            },
            onAfterReceive: (_, node, message) => hookCalls.Add($"after {node.Id} {(int)message.StatusCode}"));
        var request = CreateRequest(fixture.RootNode, SingleQuery);
        var nodeId = fixture.RootNode.Id;

        // act
        var direct = await CaptureAsync(
            fixture,
            useDefaultClient: true,
            configuration,
            (client, context) => DrainAsync(client.ExecuteAsync(context, request, TestContext.Current.CancellationToken)));
        var directHookCalls = hookCalls.ToArray();
        hookCalls.Clear();
        var legacy = await CaptureAsync(
            fixture,
            useDefaultClient: false,
            configuration,
            (client, context) => DrainAsync(client.ExecuteAsync(context, request, TestContext.Current.CancellationToken)));

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Equal(hookCalls, directHookCalls);
        Assert.Equal(new[] { $"before {nodeId}", $"after {nodeId} 200" }, directHookCalls);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/graphql-response+json; charset=utf-8, application/json; charset=utf-8, application/jsonl; charset=utf-8, text/event-stream; charset=utf-8
            X-Hook: before
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            {"query":"query A { field }"}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_WriteEveryBodyCompletely_When_RequestWritersAreReused()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration("A", s_baseAddress);
        var longValue = new string('a', 10_000);
        var longRequest = CreateRequest(fixture.RootNode, VariableQuery, $$"""{"id":"{{longValue}}"}""");
        var shortRequest = CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}""");

        // act
        var direct = await CaptureAsync(
            fixture,
            useDefaultClient: true,
            configuration,
            async (client, context) =>
            {
                await DrainAsync(client.ExecuteAsync(context, longRequest, TestContext.Current.CancellationToken));
                await DrainAsync(client.ExecuteAsync(context, shortRequest, TestContext.Current.CancellationToken));
            });

        // assert
        Assert.Equal(
            new[]
            {
                $$$"""{"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":"{{{longValue}}}"}}""",
                """{"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}}"""
            },
            direct.Bodies);
    }

    [Fact]
    public async Task ExecuteBufferedAsync_Should_SendSameRequests_When_ClientWritesBodiesDirectly()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration("A", s_baseAddress);
        SourceSchemaClientRequest[] requests =
        [
            CreateRequest(fixture.RootNode, SingleQuery),
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}"""),
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}""", """{"id":2}""")
        ];

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            async (client, context) =>
            {
                foreach (var request in requests)
                {
                    await client.ExecuteBufferedAsync(
                        context,
                        request,
                        DisposingSink.Instance,
                        TestContext.Current.CancellationToken);
                }
            });

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        direct.Requests.MatchInlineSnapshots(
            [
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/graphql-response+json; charset=utf-8, application/json; charset=utf-8, application/jsonl; charset=utf-8, text/event-stream; charset=utf-8
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                {"query":"query A { field }"}
                """,
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/graphql-response+json; charset=utf-8, application/json; charset=utf-8, application/jsonl; charset=utf-8, text/event-stream; charset=utf-8
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                {"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}}
                """,
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                {"query":"query B($id: Int!) { field(id: $id) }","variables":[{"id":1},{"id":2}]}
                """
            ]);
    }

    [Fact]
    public async Task ExecuteBatchBufferedAsync_Should_SendSameRequest_When_ClientWritesBodyDirectly()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var configuration = new HttpSourceSchemaClientConfiguration("A", s_baseAddress);
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, SingleQuery),
            CreateRequest(fixture.RootNode, OtherVariableQuery, """{"id":2}""", """{"id":3}"""));

        // act
        var (direct, legacy) = await CaptureBothAsync(
            fixture,
            configuration,
            (client, context) => client.ExecuteBatchBufferedAsync(
                context,
                requests,
                DisposingSink.Instance,
                TestContext.Current.CancellationToken).AsTask());

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Single(direct.Requests).MatchInlineSnapshot(
            """
            POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
            Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
            Content-Type: application/json; charset=utf-8
            OperationKind: Query
            [{"query":"query A { field }"},{"query":"query C($id: Int!) { field(id: $id) }","variables":[{"id":2},{"id":3}]}]
            """);
    }

    [Fact]
    public async Task ExecuteBufferedAsync_Should_InvokeRequestHooks_When_ClientWritesBodiesDirectly()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var hookCalls = new List<string>();
        var configuration = new HttpSourceSchemaClientConfiguration(
            "A",
            s_baseAddress,
            onBeforeSend: (_, _, message) =>
            {
                message.Headers.TryAddWithoutValidation("X-Hook", "before");
                hookCalls.Add($"before {message.Content!.Headers.ContentLength}");
            },
            onAfterReceive: (_, _, message) => hookCalls.Add($"after {(int)message.StatusCode}"));
        var request = CreateRequest(fixture.RootNode, SingleQuery);
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, SingleQuery),
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}"""));

        async Task SendAsync(HttpSourceSchemaClient client, OperationPlanContext context)
        {
            await client.ExecuteBufferedAsync(
                context,
                request,
                DisposingSink.Instance,
                TestContext.Current.CancellationToken);
            await client.ExecuteBatchBufferedAsync(
                context,
                requests,
                DisposingSink.Instance,
                TestContext.Current.CancellationToken);
        }

        // act
        var direct = await CaptureAsync(fixture, useDefaultClient: true, configuration, SendAsync);
        var directHookCalls = hookCalls.ToArray();
        hookCalls.Clear();
        var legacy = await CaptureAsync(fixture, useDefaultClient: false, configuration, SendAsync);

        // assert
        Assert.Equal(legacy.Requests, direct.Requests);
        Assert.Equal(hookCalls, directHookCalls);
        directHookCalls.MatchInlineSnapshots(
            [
                "before 29",
                "after 200",
                "before 102",
                "after 200"
            ]);
        direct.Requests.MatchInlineSnapshots(
            [
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/graphql-response+json; charset=utf-8, application/json; charset=utf-8, application/jsonl; charset=utf-8, text/event-stream; charset=utf-8
                X-Hook: before
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                {"query":"query A { field }"}
                """,
                """
                POST http://localhost:5000/graphql HTTP/2.0 RequestVersionOrHigher
                Accept: application/jsonl; charset=utf-8, text/event-stream; charset=utf-8, application/graphql-response+json; charset=utf-8, application/json; charset=utf-8
                X-Hook: before
                Content-Type: application/json; charset=utf-8
                OperationKind: Query
                [{"query":"query A { field }"},{"query":"query B($id: Int!) { field(id: $id) }","variables":{"id":1}}]
                """
            ]);
    }

    [Fact]
    public async Task ExecuteBufferedAsync_Should_PassCallerTokenToHandler_When_ExecutionHasDeadline()
    {
        // arrange
        await using var fixture = await SourceSchemaClientTestFixture.CreateAsync();
        var handler = new CapturingHandler();
        using var cancellationTokenSource = new CancellationTokenSource();
        await using var client = new HttpSourceSchemaClient(
            new DefaultGraphQLHttpClient(new HttpClient(handler, disposeHandler: false), handler),
            new HttpSourceSchemaClientConfiguration("A", s_baseAddress));
        var context = fixture.CreateContext();
        var request = CreateRequest(fixture.RootNode, SingleQuery);
        var requests = ImmutableArray.Create(
            CreateRequest(fixture.RootNode, SingleQuery),
            CreateRequest(fixture.RootNode, VariableQuery, """{"id":1}"""));
        var results = new List<string>();

        // act
        foreach (var hasDeadline in new[] { true, false })
        {
            context.HasExecutionDeadline = hasDeadline;
            await client.ExecuteBufferedAsync(context, request, DisposingSink.Instance, cancellationTokenSource.Token);
            await client.ExecuteBatchBufferedAsync(
                context,
                requests,
                DisposingSink.Instance,
                cancellationTokenSource.Token);
        }

        foreach (var token in handler.Tokens)
        {
            results.Add($"callerToken={token == cancellationTokenSource.Token}");
        }

        // assert
        results.MatchInlineSnapshots(
            [
                "callerToken=True",
                "callerToken=True",
                "callerToken=False",
                "callerToken=False"
            ]);
    }

    private static async Task<(CapturingHandler Direct, CapturingHandler Legacy)> CaptureBothAsync(
        SourceSchemaClientTestFixture fixture,
        HttpSourceSchemaClientConfiguration configuration,
        Func<HttpSourceSchemaClient, OperationPlanContext, Task> send)
    {
        var direct = await CaptureAsync(fixture, useDefaultClient: true, configuration, send);
        var legacy = await CaptureAsync(fixture, useDefaultClient: false, configuration, send);
        return (direct, legacy);
    }

    private static async Task<CapturingHandler> CaptureAsync(
        SourceSchemaClientTestFixture fixture,
        bool useDefaultClient,
        HttpSourceSchemaClientConfiguration configuration,
        Func<HttpSourceSchemaClient, OperationPlanContext, Task> send)
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
        var defaultClient = new DefaultGraphQLHttpClient(httpClient, disposeInnerClient: true);
        GraphQLHttpClient graphQLClient = useDefaultClient
            ? defaultClient
            : new GraphQLHttpRequestClient(defaultClient);

        await using var client = new HttpSourceSchemaClient(
            graphQLClient,
            configuration,
            annotateOperationKind: true);

        await send(client, fixture.CreateContext());
        return handler;
    }

    private static async Task DrainAsync(IAsyncEnumerable<SourceSchemaResult> results)
    {
        await foreach (var result in results)
        {
            result.Dispose();
        }
    }

    private static async Task DrainAsync(IAsyncEnumerable<SourceSchemaBatchResult> results)
    {
        await foreach (var result in results)
        {
            result.Result.Dispose();
        }
    }

    private static SourceSchemaClientRequest CreateRequest(
        ExecutionNode node,
        string sourceText,
        params string[] variableSets)
    {
        var source = Encoding.UTF8.GetBytes(sourceText);
        var variables = ImmutableArray.CreateBuilder<VariableValues>(variableSets.Length);

        foreach (var variableSet in variableSets)
        {
            var values = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(variableSet));
            variables.Add(new VariableValues(CompactPath.Root, JsonSegment.Create(values)));
        }

        return new SourceSchemaClientRequest
        {
            Node = node,
            SchemaName = "A",
            OperationType = OperationType.Query,
            OperationSourceText = new OperationSourceText(
                "Op",
                OperationType.Query,
                source,
                OperationSourceTextHash.Compute(source)),
            OperationDocument = Utf8GraphQLOperationParser.Parse(source),
            Variables = variables.MoveToImmutable()
        };
    }

    private sealed class DisposingSink : ISourceSchemaResultSink
    {
        public static DisposingSink Instance { get; } = new();

        public bool OnResult(int requestIndex, SourceSchemaResult result)
        {
            result.Dispose();
            return true;
        }
    }

    /// <summary>
    /// A client that is not a <see cref="DefaultGraphQLHttpClient"/>, so the source schema client
    /// sends every request as a <see cref="GraphQLHttpRequest"/>.
    /// </summary>
    private sealed class GraphQLHttpRequestClient(GraphQLHttpClient innerClient) : GraphQLHttpClient
    {
        public override Task<GraphQLHttpResponse> SendAsync(
            GraphQLHttpRequest request,
            CancellationToken cancellationToken = default)
            => innerClient.SendAsync(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                innerClient.Dispose();
            }
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var text = new StringBuilder();

            text.Append($"{request.Method} {request.RequestUri} ");
            text.AppendLine($"HTTP/{request.Version} {request.VersionPolicy}");

            foreach (var header in request.Headers.NonValidated)
            {
                text.AppendLine($"{header.Key}: {header.Value}");
            }

            if (request.Content.Headers.NonValidated.TryGetValues("Content-Type", out var contentType))
            {
                text.AppendLine($"Content-Type: {contentType}");
            }

            if (request.Options.TryGetValue(GraphQLHttpRequest.OperationKindOptionsKey, out var operationKind))
            {
                text.AppendLine($"OperationKind: {operationKind}");
            }

            text.Append(body);

            Requests.Add(text.ToString());
            Bodies.Add(body);
            Tokens.Add(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"field":"a"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
