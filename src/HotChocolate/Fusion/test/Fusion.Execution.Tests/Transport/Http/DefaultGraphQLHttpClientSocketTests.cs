using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Execution;

namespace HotChocolate.Fusion.Transport.Http;

public class DefaultGraphQLHttpClientSocketTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsResultAsync_Should_ReadDocument_When_ResponseComesFromSocketsHttpHandler(
        bool streamRetrievedByHandler)
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = LoopbackServer.Start(
            "application/graphql-response+json; charset=utf-8",
            """{"data":{"number":1}}""");
        using var client = CreateClient(streamRetrievedByHandler);

        // act
        using var response = await client.SendAsync(CreateRequest(server.Uri), cancellationToken);
        using var document = await response.ReadAsResultAsync(CommonTestExtensions.CreateArena(), cancellationToken);

        // assert
        Assert.Equal("""{"data":{"number":1}}""", document.Root.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsResultStreamAsync_Should_ReadEveryRecord_When_ResponseComesFromSocketsHttpHandler(
        bool streamRetrievedByHandler)
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = LoopbackServer.Start(
            "application/jsonl; charset=utf-8",
            "{\"variableIndex\":0,\"data\":{\"number\":0}}\n{\"variableIndex\":1,\"data\":{\"number\":1}}\n");
        using var client = CreateClient(streamRetrievedByHandler);
        var records = new List<string>();

        // act
        using var response = await client.SendAsync(CreateRequest(server.Uri), cancellationToken);

        await foreach (var document in response
            .ReadAsResultStreamAsync(new FixedMemoryArenaSource(CommonTestExtensions.CreateArena()))
            .WithCancellation(cancellationToken))
        {
            records.Add(document.Root.GetRawText());
        }

        // assert
        records.MatchInlineSnapshots(
            [
                """{"variableIndex":0,"data":{"number":0}}""",
                """{"variableIndex":1,"data":{"number":1}}"""
            ]);
    }

    private static DefaultGraphQLHttpClient CreateClient(bool streamRetrievedByHandler)
    {
        HttpMessageHandler handler = new SocketsHttpHandler();

        if (streamRetrievedByHandler)
        {
            handler = new StreamRetrievingHandler { InnerHandler = handler };
        }

        return new DefaultGraphQLHttpClient(new HttpClient(handler), disposeInnerClient: true);
    }

    private static GraphQLHttpRequest CreateRequest(Uri uri)
        => new(
            new OperationRequest(
                "{ number }"u8.ToArray(),
                id: null,
                operationName: null,
                onError: null,
                VariableValues.Empty,
                JsonSegment.Empty),
            uri);

    /// <summary>
    /// Retrieves the response content stream without reading it, like a handler that inspects the response.
    /// </summary>
    private sealed class StreamRetrievingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            await response.Content.ReadAsStreamAsync(cancellationToken);
            return response;
        }
    }

    /// <summary>
    /// An HTTP/1.1 server on a loopback socket that answers one POST request with a fixed body.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _response;

        private LoopbackServer(TcpListener listener, string contentType, string body)
        {
            _listener = listener;
            Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/graphql");
            _response = RespondOnceAsync(listener, contentType, body);
        }

        public Uri Uri { get; }

        public static LoopbackServer Start(string contentType, string body)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new LoopbackServer(listener, contentType, body);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _response.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _listener.Dispose();
        }

        private static async Task RespondOnceAsync(TcpListener listener, string contentType, string body)
        {
            using var socket = await listener.AcceptSocketAsync();
            var buffer = new byte[16 * 1024];
            var received = 0;
            int headerLength;

            while ((headerLength = buffer.AsSpan(0, received).IndexOf("\r\n\r\n"u8)) < 0)
            {
                var read = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None);

                if (read == 0)
                {
                    return;
                }

                received += read;
            }

            var bodyLength = GetContentLength(Encoding.ASCII.GetString(buffer, 0, headerLength));

            while (received < headerLength + 4 + bodyLength)
            {
                var read = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None);

                if (read == 0)
                {
                    return;
                }

                received += read;
            }

            var payload = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + $"Content-Type: {contentType}\r\n"
                + $"Content-Length: {payload.Length}\r\n"
                + "Connection: close\r\n"
                + "\r\n");

            await socket.SendAsync(head, SocketFlags.None);
            await socket.SendAsync(payload, SocketFlags.None);
            socket.Shutdown(SocketShutdown.Send);
        }

        private static int GetContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    return int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
                }
            }

            return 0;
        }
    }
}
