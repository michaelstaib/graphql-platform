using System.Buffers;
using System.Net.Sockets;

#if FUSION
namespace HotChocolate.Fusion.Transport.Http;
#else
namespace HotChocolate.Transport.Http;
#endif

/// <summary>
/// A network stream that, after an asynchronous read has filled the caller's buffer, takes the bytes
/// the socket already holds with one receive and serves the following reads from them.
/// A read that has to wait for data goes to the socket unchanged.
/// </summary>
internal sealed class ReadAheadNetworkStream : NetworkStream
{
    /// <summary>
    /// The largest number of bytes that one receive takes from the socket ahead of the caller.
    /// </summary>
    internal const int ReadAheadSize = 64 * 1024;

    private byte[]? _buffer;
    private int _offset;
    private int _count;
    private bool _moreExpected;

    /// <summary>
    /// Initializes a new instance of <see cref="ReadAheadNetworkStream"/> that owns
    /// <paramref name="socket"/>.
    /// </summary>
    public ReadAheadNetworkStream(Socket socket)
        : base(socket, ownsSocket: true)
    {
    }

    public override bool DataAvailable => _count > 0 || base.DataAvailable;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_count > 0)
        {
            return new ValueTask<int>(Take(buffer.Span));
        }

        if (_moreExpected && buffer.Length is > 0 and < ReadAheadSize)
        {
            _moreExpected = false;

            if (TryReceiveAhead(buffer.Span, out var taken))
            {
                return new ValueTask<int>(taken);
            }
        }

        var read = base.ReadAsync(buffer, cancellationToken);

        if (read.IsCompletedSuccessfully)
        {
            var received = read.Result;

            if (buffer.Length > 0)
            {
                _moreExpected = received == buffer.Length;
            }

            return new ValueTask<int>(received);
        }

        _moreExpected = false;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
    }

    public override int Read(Span<byte> buffer)
    {
        if (_count > 0)
        {
            return Take(buffer);
        }

        _moreExpected = false;
        return base.Read(buffer);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        if (_count > 0)
        {
            return Take(buffer.AsSpan(offset, count));
        }

        _moreExpected = false;
        return base.Read(buffer, offset, count);
    }

    public override IAsyncResult BeginRead(
        byte[] buffer,
        int offset,
        int count,
        AsyncCallback? callback,
        object? state)
        => TaskToAsyncResult.Begin(ReadAsync(buffer, offset, count, CancellationToken.None), callback, state);

    public override int EndRead(IAsyncResult asyncResult)
        => TaskToAsyncResult.End<int>(asyncResult);

    private bool TryReceiveAhead(Span<byte> destination, out int taken)
    {
        taken = 0;
        byte[]? rented = null;

        try
        {
            var socket = Socket;

            if (socket.Available <= destination.Length)
            {
                return false;
            }

            rented = ArrayPool<byte>.Shared.Rent(ReadAheadSize);
            var received = socket.Receive(rented.AsSpan(), SocketFlags.None, out var error);

            if (error is not SocketError.Success || received <= 0)
            {
                return false;
            }

            taken = Math.Min(received, destination.Length);
            rented.AsSpan(0, taken).CopyTo(destination);
            _moreExpected = received == rented.Length;

            if (taken < received)
            {
                _buffer = rented;
                _offset = taken;
                _count = received - taken;
                rented = null;
            }

            return true;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // The read that follows reports the state of the socket.
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private int Take(Span<byte> destination)
    {
        var buffer = _buffer!;
        var taken = Math.Min(_count, destination.Length);
        buffer.AsSpan(_offset, taken).CopyTo(destination);
        _offset += taken;
        _count -= taken;

        if (_count == 0)
        {
            _buffer = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return taken;
    }
}
