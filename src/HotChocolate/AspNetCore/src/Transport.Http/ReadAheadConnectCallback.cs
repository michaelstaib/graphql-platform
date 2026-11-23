using System.Net.Sockets;

#if FUSION
namespace HotChocolate.Fusion.Transport.Http;
#else
namespace HotChocolate.Transport.Http;
#endif

/// <summary>
/// Provides a <see cref="SocketsHttpHandler.ConnectCallback"/> whose connections receive a large
/// response with fewer reads from the socket.
/// </summary>
public static class ReadAheadConnectCallback
{
    /// <summary>
    /// Connects to the endpoint of <paramref name="context"/> and returns the stream of the connection.
    /// </summary>
    /// <param name="context">The connection that the handler asks for.</param>
    /// <param name="cancellationToken">The token that cancels the connection attempt.</param>
    /// <returns>The stream of the connected socket. Disposing the stream closes the socket.</returns>
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
            return new ReadAheadNetworkStream(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
