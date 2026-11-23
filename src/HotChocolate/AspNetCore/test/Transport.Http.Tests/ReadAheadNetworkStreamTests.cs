using System.Net;
using System.Net.Sockets;

namespace HotChocolate.Transport.Http;

public class ReadAheadNetworkStreamTests
{
    private const int CallerBufferSize = 4096;
    private const int PayloadSize = 32_000;

    [Fact]
    public async Task ReadAsync_Should_ReturnBytesInOrder_When_ResponseIsLargerThanCallerBuffer()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectAsync(cancellationToken);
        await using var stream = new ReadAheadNetworkStream(client);
        var payload = CreatePayload(300_000);
        var received = new MemoryStream();
        var buffer = new byte[CallerBufferSize];

        // act
        var send = SendAndCloseAsync(server, payload, cancellationToken);
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            received.Write(buffer, 0, read);
        }

        await send;

        // assert
        Assert.Equal(payload, received.ToArray());
    }

    [Fact]
    public async Task ReadAsync_Should_TakeQueuedBytesFromSocket_When_PreviousReadFilledBuffer()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectAsync(cancellationToken);
        using var serverSocket = server;
        await using var stream = new ReadAheadNetworkStream(client);
        var payload = CreatePayload(PayloadSize);
        var buffer = new byte[CallerBufferSize];
        await SendAsync(server, payload, cancellationToken);
        await WaitForAvailableAsync(client, PayloadSize, cancellationToken);

        // act
        var first = await stream.ReadAsync(buffer, cancellationToken);
        var second = await stream.ReadAsync(buffer, cancellationToken);

        // assert
        Assert.Equal(CallerBufferSize, first);
        Assert.Equal(CallerBufferSize, second);
        Assert.Equal(payload[CallerBufferSize..(2 * CallerBufferSize)], buffer);
        Assert.Equal(0, client.Available);
        Assert.True(stream.DataAvailable);
    }

    [Fact]
    public async Task ReadAsync_Should_WaitForData_When_SocketIsEmptyAfterBufferWasFilled()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectAsync(cancellationToken);
        using var serverSocket = server;
        await using var stream = new ReadAheadNetworkStream(client);
        var buffer = new byte[CallerBufferSize];
        await SendAsync(server, CreatePayload(CallerBufferSize), cancellationToken);
        await WaitForAvailableAsync(client, CallerBufferSize, cancellationToken);
        var first = await stream.ReadAsync(buffer, cancellationToken);

        // act
        var pending = stream.ReadAsync(buffer, cancellationToken).AsTask();
        var completedBeforeSend = pending.IsCompleted;
        await SendAsync(server, [1, 2, 3], cancellationToken);
        var second = await pending;

        // assert
        Assert.Equal(CallerBufferSize, first);
        Assert.False(completedBeforeSend);
        Assert.Equal(3, second);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer[..3]);
    }

    [Fact]
    public async Task ReadAsync_Should_CompleteZeroByteRead_When_BytesAreBuffered()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectAsync(cancellationToken);
        using var serverSocket = server;
        await using var stream = new ReadAheadNetworkStream(client);
        await BufferPayloadAsync(stream, client, server, cancellationToken);

        // act
        var zeroByteRead = stream.ReadAsync(Memory<byte>.Empty, cancellationToken);
        var completed = zeroByteRead.IsCompletedSuccessfully;
        var read = await zeroByteRead;

        // assert
        Assert.True(completed);
        Assert.Equal(0, read);
        Assert.True(stream.DataAvailable);
    }

    [Fact]
    public async Task Read_Should_ReturnBytesInOrder_When_BytesAreBufferedAndThenReadFromSocket()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectAsync(cancellationToken);
        using var serverSocket = server;
        await using var stream = new ReadAheadNetworkStream(client);
        var payload = await BufferPayloadAsync(stream, client, server, cancellationToken);
        var buffered = payload.Length - (2 * CallerBufferSize);
        var received = new MemoryStream();
        var buffer = new byte[CallerBufferSize];

        // act
        received.Write(buffer, 0, stream.Read(buffer, 0, buffer.Length));
        received.WriteByte((byte)stream.ReadByte());

        while (received.Length < buffered)
        {
            received.Write(buffer, 0, stream.Read(buffer.AsSpan()));
        }

        await SendAsync(server, [1, 2, 3], cancellationToken);
        await WaitForAvailableAsync(client, 3, cancellationToken);
        var fromSocketWithSpan = stream.Read(buffer.AsSpan(0, 1));
        var fromSocketWithArray = stream.Read(buffer, 1, 2);

        // assert
        Assert.Equal(payload[(2 * CallerBufferSize)..], received.ToArray());
        Assert.Equal(1, fromSocketWithSpan);
        Assert.Equal(2, fromSocketWithArray);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer[..3]);
    }

    [Fact]
    public async Task EndRead_Should_ReturnBufferedBytes_When_BytesAreBuffered()
    {
        // arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectAsync(cancellationToken);
        using var serverSocket = server;
        await using var stream = new ReadAheadNetworkStream(client);
        var payload = await BufferPayloadAsync(stream, client, server, cancellationToken);
        var buffer = new byte[CallerBufferSize];

        // act
        var read = await Task<int>.Factory.FromAsync(
            stream.BeginRead,
            stream.EndRead,
            buffer,
            0,
            buffer.Length,
            state: null);

        // assert
        Assert.Equal(CallerBufferSize, read);
        Assert.Equal(payload[(2 * CallerBufferSize)..(3 * CallerBufferSize)], buffer);
    }

    // Sends a payload and reads twice, so that the stream holds the payload from 2 * CallerBufferSize on.
    private static async Task<byte[]> BufferPayloadAsync(
        ReadAheadNetworkStream stream,
        Socket client,
        Socket server,
        CancellationToken cancellationToken)
    {
        var payload = CreatePayload(PayloadSize);
        var buffer = new byte[CallerBufferSize];

        await SendAsync(server, payload, cancellationToken);
        await WaitForAvailableAsync(client, PayloadSize, cancellationToken);
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        await stream.ReadExactlyAsync(buffer, cancellationToken);

        return payload;
    }

    private static async Task<(Socket Client, Socket Server)> ConnectAsync(CancellationToken cancellationToken)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);

        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await client.ConnectAsync(listener.LocalEndPoint!, cancellationToken);
        var server = await listener.AcceptAsync(cancellationToken);
        server.NoDelay = true;

        return (client, server);
    }

    private static async Task SendAsync(Socket socket, byte[] bytes, CancellationToken cancellationToken)
    {
        var sent = 0;

        while (sent < bytes.Length)
        {
            sent += await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, cancellationToken);
        }
    }

    private static async Task SendAndCloseAsync(Socket socket, byte[] bytes, CancellationToken cancellationToken)
    {
        using (socket)
        {
            await SendAsync(socket, bytes, cancellationToken);
            socket.Shutdown(SocketShutdown.Send);
        }
    }

    private static async Task WaitForAvailableAsync(Socket socket, int count, CancellationToken cancellationToken)
    {
        while (socket.Available < count)
        {
            await Task.Delay(1, cancellationToken);
        }
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];

        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31 + (i >> 8));
        }

        return payload;
    }
}
