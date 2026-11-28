using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Transport.Http;

namespace HotChocolate.Fusion.Transport;

public class GraphQLHttpResponseArenaReadTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(128, 1, false)]
    [InlineData(1024, 1024, false)]
    [InlineData(1025, 1024, false)]
    [InlineData(3072, 4096, false)]
    [InlineData(3073, 4096, false)]
    [InlineData(32768, 4096, false)]
    [InlineData(3073, 137, true)]
    public async Task ReadAsResult_Should_PreservePayload_When_ReadsAreFragmented(
        int payloadLength,
        int fragmentLength,
        bool asynchronous)
    {
        // arrange
        var payload = CreatePayload(payloadLength);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), fragmentLength, asynchronous);
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.Equal(payloadLength, stream.BytesRead);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(137, "")]
    [InlineData(137, " \r\n")]
    [InlineData(4096, "")]
    [InlineData(4096, " \r\n")]
    public async Task ReadAsResult_Should_NotAwaitEndOfBody_When_RootValueHasEnded(
        int fragmentLength,
        string trailer)
    {
        // arrange
        var payload = CreatePayload(3073);
        var endOfBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload + trailer), fragmentLength, asynchronous: true)
        {
            EndOfBody = endOfBody.Task
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response
            .ReadAsResultAsync(arena, TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.False(stream.EndOfBodyRequested);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task ReadAsResult_Should_ReadToEndOfBody_When_DataFollowsRootValueInSameRead()
    {
        // arrange
        var payload = Encoding.UTF8.GetBytes("{\"data\":{}} {\"data\":{}}");
        var stream = new FragmentedStream(payload, 4096, asynchronous: false);
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload.Length, stream.BytesRead);
        Assert.Equal(2, stream.ReadCount);
    }

    [Fact]
    public async Task ReadAsResult_Should_NotAwaitEndOfBody_When_PayloadIsSplitAtAnyPosition()
    {
        // arrange
        var payload = CreateTrickyPayload();
        var bytes = Encoding.UTF8.GetBytes(payload + " \r\n");
        var failedSplits = new List<int>();

        // act
        for (var split = 1; split <= bytes.Length; split++)
        {
            var stream = new FragmentedStream(bytes, int.MaxValue, asynchronous: false)
            {
                SplitPosition = split,
                EndOfBody = Task.CompletedTask
            };
            using var response = CreateResponse(stream, contentLength: null, "utf-8");
            using var arena = new MemoryArena();
            using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
            arena.Seal();

            // The read that holds the end of the root value and only whitespace after it is the last read.
            var expectedBytesRead = split >= payload.Length ? split : bytes.Length;

            if (document.Root.GetRawText() != payload
                || stream.BytesRead != expectedBytesRead
                || stream.EndOfBodyRequested)
            {
                failedSplits.Add(split);
            }
        }

        // assert
        Assert.Empty(failedSplits);
    }

    [Fact]
    public async Task ReadAsResult_Should_NotReadNextFragment_When_RootValueEndsAtReadBoundary()
    {
        // arrange
        const string root = "{\"data\":{}}";
        var payload = Encoding.UTF8.GetBytes(root + " {\"data\":{}}");
        var stream = new FragmentedStream(payload, 4096, asynchronous: false)
        {
            SplitPosition = root.Length
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(root, document.Root.GetRawText());
        Assert.Equal(root.Length, stream.BytesRead);
        Assert.Equal(1, stream.ReadCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public async Task ReadAsResult_Should_NotAwaitEndOfBody_When_RootValueIsArray(int fragmentLength)
    {
        // arrange
        const string payload = "[{\"data\":\"]\"},[1,[2]],\"\\\"]\"]";
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload + "\n"), fragmentLength, asynchronous: false)
        {
            EndOfBody = Task.CompletedTask
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.False(stream.EndOfBodyRequested);
    }

    [Theory]
    [InlineData("\"{\\\"data\\\":{}}\"")]
    [InlineData("\"}\"")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("null")]
    public async Task ReadAsResult_Should_ReadToEndOfBody_When_RootValueIsScalar(string payload)
    {
        // arrange
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), 4096, asynchronous: false);
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.Equal(payload.Length, stream.BytesRead);
        Assert.Equal(2, stream.ReadCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadAsResult_Should_ReadToEndOfBody_When_ResponseIsNotHttp1(int majorVersion)
    {
        // arrange
        var payload = CreatePayload(512);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), 4096, asynchronous: false);
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        response.HttpResponseMessage.Version = new Version(majorVersion, 0);
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.Equal(2, stream.ReadCount);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(137)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public async Task ReadAsResult_Should_ReadToEndOfBody_When_ContentLengthIsDeclared(int fragmentLength)
    {
        // arrange
        var payload = CreatePayload(20_000);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var stream = new FragmentedStream(bytes, fragmentLength, asynchronous: false)
        {
            EndOfBody = Task.CompletedTask
        };
        using var response = CreateResponse(stream, bytes.Length, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.True(stream.EndOfBodyRequested);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(32768)]
    public async Task ReadAsResult_Should_PreservePayload_When_ContentLengthIsDeclared(int contentLength)
    {
        // arrange
        var payload = CreatePayload(128);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), 31, asynchronous: false);
        using var response = CreateResponse(stream, contentLength, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(40_000, 39_999, int.MaxValue)]
    [InlineData(40_000, 39_999, 137)]
    [InlineData(150_000, 20_000, 4096)]
    [InlineData(140_000, 131_071, int.MaxValue)]
    public async Task ReadAsResult_Should_PreservePayload_When_BodyIsLongerThanDeclared(
        int payloadLength,
        int contentLength,
        int fragmentLength)
    {
        // arrange
        var payload = CreatePayload(payloadLength);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), fragmentLength, asynchronous: false);
        using var response = CreateResponse(stream, contentLength, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.Equal(payloadLength, stream.BytesRead);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(16 * 1024)]
    [InlineData((16 * 1024) + 1)]
    [InlineData(131_071)]
    [InlineData(131_072)]
    [InlineData(131_073)]
    public async Task ReadAsResult_Should_PreservePayload_When_DeclaredLengthIsNearALimit(int payloadLength)
    {
        // arrange
        // 131,072 bytes are one arena page, and a body that declares fewer is read into a single chunk
        var payload = CreatePayload(payloadLength);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), 4096, asynchronous: false);
        using var response = CreateResponse(stream, payloadLength, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.Equal(payloadLength, stream.BytesRead);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(20_000)]
    [InlineData(100_000)]
    public async Task ReadAsResult_Should_ReadBodyWithOneRead_When_ContentLengthIsDeclared(int payloadLength)
    {
        // arrange
        var payload = CreatePayload(payloadLength);
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), int.MaxValue, asynchronous: false);
        using var response = CreateResponse(stream, payloadLength, "utf-8");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        // the first read is offered the whole body and the second read finds the end of the body
        Assert.Equal(payload, document.Root.GetRawText());
        Assert.Equal(2, stream.ReadCount);
    }

    [Fact]
    public async Task ReadAsResult_Should_RentTheDeclaredLength_When_ContentLengthIsDeclared()
    {
        // arrange
        var longPayload = CreatePayload(100_000);
        var shortPayload = CreatePayload(200);
        using var longArena = new MemoryArena();
        using var shortArena = new MemoryArena();
        using var longResponse = CreateResponse(
            new FragmentedStream(Encoding.UTF8.GetBytes(longPayload), 4096, asynchronous: false),
            longPayload.Length,
            "utf-8");
        using var shortResponse = CreateResponse(
            new FragmentedStream(Encoding.UTF8.GetBytes(shortPayload), 4096, asynchronous: false),
            shortPayload.Length,
            "utf-8");

        // act
        using var longDocument = await longResponse.ReadAsResultAsync(
            longArena,
            TestContext.Current.CancellationToken);
        using var shortDocument = await shortResponse.ReadAsResultAsync(
            shortArena,
            TestContext.Current.CancellationToken);

        // assert
        // both payloads have the same tokens, so their arenas differ by the bytes of the longer value
        Assert.Equal(1, longArena.RentedPageCount);
        Assert.Equal(GetUsedBytes(shortArena) + 100_000 - 200, GetUsedBytes(longArena));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4096)]
    public async Task ReadAsResult_Should_TranscodePayload_When_ResponseUsesUtf16(int fragmentLength)
    {
        // arrange
        var value = string.Concat(Enumerable.Repeat("é🔭", 4096));
        var payload = "{\"data\":{\"value\":\"" + value + "\"}}";
        var stream = new FragmentedStream(Encoding.Unicode.GetBytes(payload), fragmentLength, asynchronous: false);
        using var response = CreateResponse(stream, contentLength: null, "utf-16");
        using var arena = new MemoryArena();

        // act
        using var document = await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken);
        arena.Seal();

        // assert
        Assert.Equal(value, document.Root.GetProperty("data").GetProperty("value").GetString());
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"data\":")]
    public async Task ReadAsResult_Should_DisposeStream_When_PayloadIsInvalid(string payload)
    {
        // arrange
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(payload), 3, asynchronous: false);
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        var exception = await Record.ExceptionAsync(async () =>
            await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken));
        arena.Seal();

        // assert
        Assert.IsAssignableFrom<JsonException>(exception);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task ReadAsResult_Should_DisposeStream_When_ReadIsCanceled()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(CreatePayload(3073)), 137, asynchronous: true)
        {
            BeforeRead = position =>
            {
                if (position > 0)
                {
                    cancellation.Cancel();
                }
            }
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        var exception = await Record.ExceptionAsync(async () =>
            await response.ReadAsResultAsync(arena, cancellation.Token));
        arena.Seal();

        // assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(137, stream.BytesRead);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task ReadAsResult_Should_ObserveCancellation_When_StreamIgnoresToken()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var stream = new FragmentedStream(Encoding.UTF8.GetBytes(CreatePayload(3073)), 137, asynchronous: false)
        {
            IgnoreCancellation = true,
            AfterRead = _ => cancellation.Cancel()
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        using var arena = new MemoryArena();

        // act
        var exception = await Record.ExceptionAsync(async () =>
        {
            using var document = await response.ReadAsResultAsync(arena, cancellation.Token);
        });
        arena.Seal();

        // assert
        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(137, stream.BytesRead);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task ReadAsResult_Should_ObserveCancellation_When_FullCapacityRequiresEofProbe()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var capacity = 0L;
        for (var i = 0; i < SourceResultDocument.DataMaxChunks; i++)
        {
            capacity += SourceResultDocument.GetDataChunkSize(i);
        }
        var stream = new CapacityReadStream(capacity, new IOException("The EOF probe must not be read."))
        {
            AfterRead = position =>
            {
                if (position == capacity)
                {
                    cancellation.Cancel();
                }
            }
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        var arena = new UnparsedMemoryArena();

        // act
        var exception = await Record.ExceptionAsync(async () =>
            await response.ReadAsResultAsync(arena, cancellation.Token));

        // assert
        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(capacity, stream.BytesRead);
    }

    [Fact]
    public async Task ReadAsResult_Should_ObserveReadFailure_When_FullCapacityRequiresEofProbe()
    {
        // arrange
        var capacity = 0L;
        for (var i = 0; i < SourceResultDocument.DataMaxChunks; i++)
        {
            capacity += SourceResultDocument.GetDataChunkSize(i);
        }
        var expected = new IOException("The final body read failed.");
        var stream = new CapacityReadStream(capacity, expected);
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        var arena = new UnparsedMemoryArena();

        // act
        var exception = await Record.ExceptionAsync(async () =>
            await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken));

        // assert
        Assert.Same(expected, exception);
        Assert.Equal(capacity, stream.BytesRead);
    }

    [Fact]
    public async Task ReadAsResult_Should_NotProbeForEndOfBody_When_RootValueEndsAtFullCapacity()
    {
        // arrange
        var capacity = 0L;
        for (var i = 0; i < SourceResultDocument.DataMaxChunks; i++)
        {
            capacity += SourceResultDocument.GetDataChunkSize(i);
        }
        var stream = new CapacityReadStream(capacity, new IOException("The EOF probe must not be read."))
        {
            WritesRootValue = true
        };
        using var response = CreateResponse(stream, contentLength: null, "utf-8");
        var arena = new UnparsedMemoryArena();

        // act
        var exception = await Record.ExceptionAsync(async () =>
            await response.ReadAsResultAsync(arena, TestContext.Current.CancellationToken));

        // assert
        // The payload is read in full and cannot be parsed.
        Assert.IsAssignableFrom<JsonException>(exception);
        Assert.Equal(capacity, stream.BytesRead);
    }

    private static long GetUsedBytes(MemoryArena arena)
    {
        // a zero-length rental reports the cursor of the current page.
        var cursor = arena.Rent(0);
        return ((long)(arena.RentedPageCount - 1) * JsonMemory.BufferSize) + cursor.Offset;
    }

    private static string CreateTrickyPayload()
    {
        var text = new string('a', 250);
        var payload =
            "{\"data\":{\"a\":\"" + text + "}{][\\\"\",\"b\":\"" + text + "]}\\\\\","
            + "\"c\":[1,{\"d\":\"\\\\\\\"" + text + "\"},[[]]],\"e\":\"\\n" + text + text + "\","
            + "\"f\":[[[{\"" + text + "}]\":[]}]]]}}";
        using var validated = JsonDocument.Parse(payload);
        return payload;
    }

    private static string CreatePayload(int length)
    {
        const string prefix = "{\"data\":{\"value\":\"";
        const string suffix = "\"}}";
        return prefix + new string('a', length - prefix.Length - suffix.Length) + suffix;
    }

    private static GraphQLHttpResponse CreateResponse(Stream stream, long? contentLength, string charset)
    {
        var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/graphql-response+json")
        {
            CharSet = charset
        };
        message.Content.Headers.ContentLength = contentLength;
        return new GraphQLHttpResponse(message);
    }

    private sealed class FragmentedStream(byte[] payload, int fragmentLength, bool asynchronous) : Stream
    {
        public int BytesRead { get; private set; }

        public int ReadCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool EndOfBodyRequested { get; private set; }

        public Task? EndOfBody { get; init; }

        public Action<int>? BeforeRead { get; init; }

        public Action<int>? AfterRead { get; init; }

        public bool IgnoreCancellation { get; init; }

        public int? SplitPosition { get; init; }

        public override bool CanRead => !IsDisposed;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (asynchronous)
            {
                await Task.Yield();
            }

            BeforeRead?.Invoke(BytesRead);
            if (!IgnoreCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (EndOfBody is not null && BytesRead == payload.Length)
            {
                EndOfBodyRequested = true;
                await EndOfBody;
            }
            ReadCount++;
            var length = Math.Min(Math.Min(buffer.Length, fragmentLength), payload.Length - BytesRead);
            if (SplitPosition > BytesRead)
            {
                length = Math.Min(length, SplitPosition.Value - BytesRead);
            }
            payload.AsMemory(BytesRead, length).CopyTo(buffer);
            BytesRead += length;
            AfterRead?.Invoke(BytesRead);
            return length;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UnparsedMemoryArena : IMemoryArena
    {
        private readonly byte[] _buffer = new byte[128 * 1024];

        public MemorySegment Rent(int size) => new(_buffer, 0, size);

        public MemorySegment[] RentSegmentTable(int minLength) => new MemorySegment[minLength];

        public void GrowSegmentTable(ref MemorySegment[] table) => Array.Resize(ref table, table.Length * 2);
    }

    private sealed class CapacityReadStream(long capacity, IOException exception) : Stream
    {
        public long BytesRead { get; private set; }
        public Action<long>? AfterRead { get; init; }
        public bool WritesRootValue { get; init; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (BytesRead == capacity)
            {
                throw exception;
            }

            var count = (int)Math.Min(buffer.Length, capacity - BytesRead);
            if (WritesRootValue)
            {
                // The body is an object without properties that is as long as the capacity.
                var body = buffer.Span[..count];
                body.Fill((byte)' ');
                if (BytesRead == 0)
                {
                    body[0] = (byte)'{';
                }
                if (BytesRead + count == capacity)
                {
                    body[^1] = (byte)'}';
                }
            }
            BytesRead += count;
            AfterRead?.Invoke(BytesRead);
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
