using System.Net;
using System.Net.Sockets;
using System.Text;
using HotChocolate.Fusion.Transport.Http;

namespace HotChocolate.Fusion.Transport;

public class HttpTransportUtilitiesTests
{
    private const string ResponseBody = """{"data":{"field":"a"}}""";

    [Fact]
    public async Task TryReadAsStream_Should_TakeResponseStream_When_ContentComesFromSocketsHttpHandler()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RespondOnceAsync(listener, ResponseBody, cancellationToken);
        using var httpClient = new HttpClient(new SocketsHttpHandler());
        using var response = await httpClient.GetAsync(
            new Uri($"http://127.0.0.1:{port}/graphql"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        // act
        var taken = HttpTransportUtilities.TryReadAsStream(response.Content, cancellationToken, out var stream);
        var body = stream is null ? null : await ReadToEndAsync(stream, cancellationToken);
        await server;

        // assert
        Assert.True(taken);
        Assert.Equal(ResponseBody, body);
    }

    [Fact]
    public async Task ReadAsStreamAsync_Should_ReadContentAsynchronously_When_ContentIsNotAConnectionResponse()
    {
        // arrange
        using var content = new StringContent(ResponseBody, Encoding.UTF8, "application/json");

        // act
        var taken = HttpTransportUtilities.TryReadAsStream(
            content,
            TestContext.Current.CancellationToken,
            out _);
        var stream = await HttpTransportUtilities.ReadAsStreamAsync(
            content,
            TestContext.Current.CancellationToken);
        var body = await ReadToEndAsync(stream, TestContext.Current.CancellationToken);

        // assert
        Assert.False(taken);
        Assert.Equal(ResponseBody, body);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private static async Task RespondOnceAsync(
        TcpListener listener,
        string body,
        CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        var buffer = new byte[4096];
        var received = 0;

        // The request is a GET without a body, so it ends with the blank line after its headers.
        while (!HasHeaderTerminator(buffer, received))
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, cancellationToken);

            if (read == 0)
            {
                return;
            }

            received += read;
        }

        var payload = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Content-Type: application/json\r\n"
            + $"Content-Length: {payload.Length}\r\n"
            + "Connection: close\r\n"
            + "\r\n");

        await socket.SendAsync(head, SocketFlags.None, cancellationToken);
        await socket.SendAsync(payload, SocketFlags.None, cancellationToken);
        socket.Shutdown(SocketShutdown.Send);
    }

    private static bool HasHeaderTerminator(byte[] buffer, int length)
        => buffer.AsSpan(0, length).EndsWith("\r\n\r\n"u8);
}
