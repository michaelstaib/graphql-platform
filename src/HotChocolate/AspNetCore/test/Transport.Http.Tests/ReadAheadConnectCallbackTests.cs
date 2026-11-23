using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HotChocolate.Transport.Http;

public class ReadAheadConnectCallbackTests
{
    [Fact]
    public async Task ConnectAsync_Should_ReuseConnection_When_ResponsesAreLargerThanCallerBuffer()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var body = CreateBody(200_000);
        await using var server = new RawHttpServer(body, trailingBytes: 0);
        using var client = CreateClient();

        // act
        var first = await GetAsync(client, server.Address, cancellationToken);
        var second = await GetAsync(client, server.Address, cancellationToken);

        // assert
        Assert.Equal(body, first);
        Assert.Equal(body, second);
        Assert.Equal(1, server.Connections);
    }

    [Fact]
    public async Task ConnectAsync_Should_UseNewConnection_When_ServerSentBytesAfterResponse()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var body = CreateBody(20_000);
        await using var server = new RawHttpServer(body, trailingBytes: 16);
        using var client = CreateClient();

        // act
        var first = await GetAsync(client, server.Address, cancellationToken);
        var second = await GetAsync(client, server.Address, cancellationToken);

        // assert
        Assert.Equal(body, first);
        Assert.Equal(body, second);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task ConnectAsync_Should_ThrowArgumentNullException_When_ContextIsNull()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => ReadAheadConnectCallback.ConnectAsync(null!, cancellationToken).AsTask());

        // assert
        Assert.Equal("context", exception.ParamName);
    }

    private static HttpClient CreateClient()
        => new(new SocketsHttpHandler { ConnectCallback = ReadAheadConnectCallback.ConnectAsync });

    // Reads the response body with a buffer that is smaller than the body.
    private static async Task<byte[]> GetAsync(HttpClient client, Uri address, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            address,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var received = new MemoryStream();
        var buffer = new byte[4096];
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            received.Write(buffer, 0, read);
        }

        return received.ToArray();
    }

    private static byte[] CreateBody(int length)
    {
        var body = new byte[length];

        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)('a' + (i % 26));
        }

        return body;
    }

    // Answers every request on a connection with the same body, followed by the given number of extra bytes.
    private sealed class RawHttpServer : IAsyncDisposable
    {
        private static ReadOnlySpan<byte> HeaderEnd => "\r\n\r\n"u8;

        private readonly Socket _listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _connectionTasks = [];
        private readonly byte[] _response;
        private readonly Task _acceptTask;
        private int _connections;

        public RawHttpServer(byte[] body, int trailingBytes)
        {
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {body.Length}\r\n\r\n");
            _response = new byte[header.Length + body.Length + trailingBytes];
            header.CopyTo(_response, 0);
            body.CopyTo(_response, header.Length);
            _response.AsSpan(header.Length + body.Length).Fill((byte)'!');

            _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _listener.Listen(8);
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndPoint!).Port}/");
            _acceptTask = AcceptAsync();
        }

        public Uri Address { get; }

        public int Connections => Volatile.Read(ref _connections);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _acceptTask;
            await Task.WhenAll(_connectionTasks);
            _listener.Dispose();
            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var socket = await _listener.AcceptAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                    _connectionTasks.Add(ServeAsync(socket));
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ServeAsync(Socket socket)
        {
            using (socket)
            {
                var buffer = new byte[4096];
                var matched = 0;

                try
                {
                    while (true)
                    {
                        var read = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, _stop.Token);

                        if (read == 0)
                        {
                            return;
                        }

                        for (var i = 0; i < read; i++)
                        {
                            matched = Advance(matched, buffer[i]);

                            if (matched == HeaderEnd.Length)
                            {
                                matched = 0;
                                await SendAsync(socket);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException)
                {
                }
            }
        }

        private async Task SendAsync(Socket socket)
        {
            var sent = 0;

            while (sent < _response.Length)
            {
                sent += await socket.SendAsync(_response.AsMemory(sent), SocketFlags.None, _stop.Token);
            }
        }

        private static int Advance(int matched, byte value)
        {
            if (value == HeaderEnd[matched])
            {
                return matched + 1;
            }

            return value == HeaderEnd[0] ? 1 : 0;
        }
    }
}
