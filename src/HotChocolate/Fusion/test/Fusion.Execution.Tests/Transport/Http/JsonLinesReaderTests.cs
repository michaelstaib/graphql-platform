using System.Net;
using System.Text;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Text.Json;

namespace HotChocolate.Fusion.Transport.Http;

public class JsonLinesReaderTests
{
    public enum ArenaSourceKind
    {
        Fixed,
        PerDocument
    }

    [Theory]
    [InlineData(1, ArenaSourceKind.Fixed)]
    [InlineData(1, ArenaSourceKind.PerDocument)]
    [InlineData(7, ArenaSourceKind.Fixed)]
    [InlineData(7, ArenaSourceKind.PerDocument)]
    [InlineData(100, ArenaSourceKind.Fixed)]
    [InlineData(100, ArenaSourceKind.PerDocument)]
    [InlineData(1500, ArenaSourceKind.Fixed)]
    [InlineData(1500, ArenaSourceKind.PerDocument)]
    [InlineData(4096, ArenaSourceKind.Fixed)]
    [InlineData(4096, ArenaSourceKind.PerDocument)]
    [InlineData(int.MaxValue, ArenaSourceKind.Fixed)]
    [InlineData(int.MaxValue, ArenaSourceKind.PerDocument)]
    public async Task GetAsyncEnumerator_Should_YieldEveryRecord_When_RecordsAreSplitAcrossReads(
        int maxBytesPerRead,
        ArenaSourceKind kind)
    {
        // arrange
        string[] values = ["a", new string('b', 300), "c", new string('d', 2000), "e", new string('f', 5000), "g"];
        var body = string.Concat(values.Select(value => CreateRecord(value) + "\n"));

        // act
        var result = await ReadValuesAsync(body, maxBytesPerRead, CreateArenaSource(kind));

        // assert
        Assert.Equal(values, result);
    }

    [Theory]
    [InlineData(1000, ArenaSourceKind.Fixed)]
    [InlineData(1000, ArenaSourceKind.PerDocument)]
    [InlineData(4096, ArenaSourceKind.Fixed)]
    [InlineData(4096, ArenaSourceKind.PerDocument)]
    [InlineData(16 * 1024, ArenaSourceKind.Fixed)]
    [InlineData(16 * 1024, ArenaSourceKind.PerDocument)]
    [InlineData(int.MaxValue, ArenaSourceKind.Fixed)]
    [InlineData(int.MaxValue, ArenaSourceKind.PerDocument)]
    public async Task GetAsyncEnumerator_Should_YieldRecord_When_RecordSpansSeveralReadChunks(
        int maxBytesPerRead,
        ArenaSourceKind kind)
    {
        // arrange
        // the records are longer than one read chunk, and the longest exceeds the single-span limit.
        string[] values =
        [
            "a",
            new string('b', 12 * 1024),
            "c",
            new string('d', 16 * 1024),
            new string('e', 40 * 1024),
            "f"
        ];
        var body = string.Concat(values.Select(value => CreateRecord(value) + "\n"));

        // act
        var result = await ReadValuesAsync(body, maxBytesPerRead, CreateArenaSource(kind));

        // assert
        Assert.Equal(values, result);
    }

    [Theory]
    [InlineData(1, ArenaSourceKind.Fixed)]
    [InlineData(1, ArenaSourceKind.PerDocument)]
    [InlineData(5, ArenaSourceKind.Fixed)]
    [InlineData(5, ArenaSourceKind.PerDocument)]
    [InlineData(int.MaxValue, ArenaSourceKind.Fixed)]
    [InlineData(int.MaxValue, ArenaSourceKind.PerDocument)]
    public async Task GetAsyncEnumerator_Should_YieldFinalRecord_When_StreamEndsWithoutNewline(
        int maxBytesPerRead,
        ArenaSourceKind kind)
    {
        // arrange
        var body = CreateRecord("a") + "\n" + CreateRecord("b");

        // act
        var result = await ReadValuesAsync(body, maxBytesPerRead, CreateArenaSource(kind));

        // assert
        Assert.Equal(["a", "b"], result);
    }

    [Theory]
    [InlineData(1, ArenaSourceKind.Fixed)]
    [InlineData(1, ArenaSourceKind.PerDocument)]
    [InlineData(3, ArenaSourceKind.Fixed)]
    [InlineData(3, ArenaSourceKind.PerDocument)]
    [InlineData(int.MaxValue, ArenaSourceKind.Fixed)]
    [InlineData(int.MaxValue, ArenaSourceKind.PerDocument)]
    public async Task GetAsyncEnumerator_Should_SkipBlankLines_When_LinesUseCarriageReturns(
        int maxBytesPerRead,
        ArenaSourceKind kind)
    {
        // arrange
        var body = "\n\r\n"
            + CreateRecord("a") + "\r\n"
            + " \t\r\n"
            + "\n"
            + CreateRecord("b") + "\r\n"
            + "  \r";

        // act
        var result = await ReadValuesAsync(body, maxBytesPerRead, CreateArenaSource(kind));

        // assert
        Assert.Equal(["a", "b"], result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n \r\n\t")]
    public async Task GetAsyncEnumerator_Should_LeaveArenaUnused_When_BodyHasNoRecords(string body)
    {
        // arrange
        var arena = new MemoryArena();

        // act
        var result = await ReadValuesAsync(body, int.MaxValue, new FixedMemoryArenaSource(arena));

        // assert
        Assert.Empty(result);
        Assert.Equal(0, GetUsedBytes(arena));
    }

    [Fact]
    public async Task GetAsyncEnumerator_Should_NotRequestAnArena_When_BodyHasOnlyBlankLines()
    {
        // arrange
        var arenaSource = new PerDocumentArenaSource();

        // act
        var result = await ReadValuesAsync("\n \r\n\t", int.MaxValue, arenaSource);

        // assert
        Assert.Empty(result);
        Assert.Empty(arenaSource.Arenas);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public async Task GetAsyncEnumerator_Should_BackEachDocumentByItsOwnArena_When_SourceIsNotFixed(
        int maxBytesPerRead)
    {
        // arrange
        string[] values = ["a", new string('b', 300), "c"];
        var body = string.Concat(values.Select(value => CreateRecord(value) + "\n")) + "\n";
        var arenaSource = new PerDocumentArenaSource();

        // act
        var result = await ReadValuesAsync(body, maxBytesPerRead, arenaSource);

        // assert
        Assert.Equal(values, result);
        Assert.Equal(3, arenaSource.Arenas.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(300)]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public async Task GetAsyncEnumerator_Should_UseRecordBytesOfArena_When_ParsingInPlace(int maxBytesPerRead)
    {
        // arrange
        var body = CreateBody(recordCount: 40, maxValueLength: 900);
        var inPlaceArena = new MemoryArena();
        var copyArena = new MemoryArena();

        // act
        await ReadValuesAsync(body, maxBytesPerRead, new FixedMemoryArenaSource(inPlaceArena));
        await ReadValuesAsync(body, maxBytesPerRead, new SharedArenaSource(copyArena));

        // assert
        // a record parsed in place keeps its line terminator, which is one byte per record.
        Assert.Equal(GetUsedBytes(copyArena) + 40, GetUsedBytes(inPlaceArena));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public async Task GetAsyncEnumerator_Should_UseRecordBytesOfArena_When_ArenaIsRentedDuringReads(
        int maxBytesPerRead)
    {
        // arrange
        // the stream rents from the arena on every read, like a concurrent fetch of the same request.
        var values = CreateValues(recordCount: 40, maxValueLength: 900);
        var body = CreateBody(values);
        var copyArena = new MemoryArena();
        await ReadValuesAsync(body, maxBytesPerRead, new SharedArenaSource(copyArena));
        var arena = new MemoryArena();
        var stream = new RentingStream(
            new DripStream(new MemoryStream(Encoding.UTF8.GetBytes(body)), maxBytesPerRead),
            arena);

        // act
        var result = await ReadValuesAsync(stream, new FixedMemoryArenaSource(arena));

        // assert
        // a record parsed in place keeps its line terminator, which is one byte per record.
        Assert.Equal(values, result);
        Assert.Equal(GetUsedBytes(copyArena) + 40 + stream.RentedBytes, GetUsedBytes(arena));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public async Task GetAsyncEnumerator_Should_YieldEveryRecord_When_RecordsFillSeveralArenaPages(
        int maxBytesPerRead)
    {
        // arrange
        var values = CreateValues(recordCount: 300, maxValueLength: 2000);
        var arena = new MemoryArena();
        arena.Rent(JsonMemory.BufferSize - 1500);

        // act
        var result = await ReadValuesAsync(CreateBody(values), maxBytesPerRead, new FixedMemoryArenaSource(arena));

        // assert
        Assert.Equal(values, result);
        Assert.InRange(arena.RentedPageCount, 3, 10);
    }

    [Fact]
    public async Task GetAsyncEnumerator_Should_RentOnlyRecordBytes_When_ReadFails()
    {
        // arrange
        var body = Encoding.UTF8.GetBytes(CreateRecord("a") + "\n");
        var expectedArena = new MemoryArena();
        await ReadValuesAsync(CreateRecord("a") + "\n", int.MaxValue, new FixedMemoryArenaSource(expectedArena));
        var arena = new MemoryArena();
        using var message = CreateMessage(new FailingStream(body));
        var reader = new JsonLinesReader(message, new FixedMemoryArenaSource(arena), Timeout.InfiniteTimeSpan);
        var values = new List<string>();

        // act
        async Task Act()
        {
            await foreach (var document in reader.WithCancellation(TestContext.Current.CancellationToken))
            {
                values.Add(GetValue(document));
            }
        }

        // assert
        await Assert.ThrowsAsync<IOException>(Act);
        Assert.Equal(["a"], values);
        Assert.Equal(GetUsedBytes(expectedArena), GetUsedBytes(arena));
    }

    [Fact]
    public async Task GetAsyncEnumerator_Should_NotIssueZeroByteReads_When_SourceIsFixed()
    {
        // arrange
        var record = CreateRecord("a") + "\n";
        var body = Encoding.UTF8.GetBytes(record + record + record);
        var stream = new CountingStream(new DripStream(new MemoryStream(body), record.Length));

        // act
        var result = await ReadValuesAsync(stream, new FixedMemoryArenaSource(new MemoryArena()));

        // assert
        // three reads return a record each and the fourth read returns the end of the stream.
        Assert.Equal(["a", "a", "a"], result);
        Assert.Equal(0, stream.ZeroByteReads);
        Assert.Equal(4, stream.NonEmptyReads);
    }

    [Fact]
    public async Task GetAsyncEnumerator_Should_WaitWithZeroByteRead_When_SourceIsPerDocument()
    {
        // arrange
        var record = CreateRecord("a") + "\n";
        var body = Encoding.UTF8.GetBytes(record + record + record);
        var stream = new CountingStream(new DripStream(new MemoryStream(body), record.Length));

        // act
        var result = await ReadValuesAsync(stream, new PerDocumentArenaSource());

        // assert
        // every read starts without a partial record, so a zero-byte read precedes each of them.
        Assert.Equal(["a", "a", "a"], result);
        Assert.Equal(4, stream.ZeroByteReads);
        Assert.Equal(4, stream.NonEmptyReads);
    }

    [Theory]
    [InlineData(ArenaSourceKind.Fixed)]
    [InlineData(ArenaSourceKind.PerDocument)]
    public async Task GetAsyncEnumerator_Should_ThrowOperationCanceled_When_CanceledBetweenRecords(
        ArenaSourceKind kind)
    {
        // arrange
        var record = CreateRecord("a") + "\n";
        var body = Encoding.UTF8.GetBytes(record + record);
        using var message = CreateMessage(new DripStream(new MemoryStream(body), record.Length));
        var reader = new JsonLinesReader(message, CreateArenaSource(kind), Timeout.InfiniteTimeSpan);
        using var cts = new CancellationTokenSource();
        await using var enumerator = reader.GetAsyncEnumerator(cts.Token);
        var hasFirst = await enumerator.MoveNextAsync();
        var value = GetValue(enumerator.Current);

        // act
        await cts.CancelAsync();
        async Task Act() => await enumerator.MoveNextAsync();

        // assert
        Assert.True(hasFirst);
        Assert.Equal("a", value);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Act);
    }

    [Theory]
    [InlineData(ArenaSourceKind.Fixed)]
    [InlineData(ArenaSourceKind.PerDocument)]
    public async Task GetAsyncEnumerator_Should_EndCleanly_When_ConsumerStopsEarly(ArenaSourceKind kind)
    {
        // arrange
        var body = CreateBody(recordCount: 10, maxValueLength: 100);
        using var message = CreateMessage(new DripStream(new MemoryStream(Encoding.UTF8.GetBytes(body)), 64));
        var reader = new JsonLinesReader(message, CreateArenaSource(kind), Timeout.InfiniteTimeSpan);
        var values = new List<string>();

        // act
        await foreach (var document in reader.WithCancellation(TestContext.Current.CancellationToken))
        {
            values.Add(GetValue(document));

            if (values.Count == 2)
            {
                break;
            }
        }

        // assert
        Assert.Equal([new string('x', 1), new string('x', 38)], values);
    }

    private static Task<List<string>> ReadValuesAsync(
        string body,
        int maxBytesPerRead,
        IMemoryArenaSource arenaSource)
        => ReadValuesAsync(
            new DripStream(new MemoryStream(Encoding.UTF8.GetBytes(body)), maxBytesPerRead),
            arenaSource);

    private static async Task<List<string>> ReadValuesAsync(Stream stream, IMemoryArenaSource arenaSource)
    {
        using var message = CreateMessage(stream);
        var reader = new JsonLinesReader(message, arenaSource, Timeout.InfiniteTimeSpan);
        var values = new List<string>();

        await foreach (var document in reader.WithCancellation(TestContext.Current.CancellationToken))
        {
            using (document)
            {
                values.Add(GetValue(document));
            }
        }

        return values;
    }

    private static HttpResponseMessage CreateMessage(Stream stream)
        => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };

    private static string GetValue(SourceResultDocument document)
        => document.Root.GetProperty("data").GetProperty("value").GetString()!;

    private static string CreateRecord(string value)
        => "{\"data\":{\"value\":\"" + value + "\"}}";

    private static string[] CreateValues(int recordCount, int maxValueLength)
        => [.. Enumerable.Range(0, recordCount).Select(i => new string('x', 1 + (i * 37 % maxValueLength)))];

    private static string CreateBody(IEnumerable<string> values)
        => string.Concat(values.Select(value => CreateRecord(value) + "\n"));

    private static string CreateBody(int recordCount, int maxValueLength)
        => CreateBody(CreateValues(recordCount, maxValueLength));

    private static IMemoryArenaSource CreateArenaSource(ArenaSourceKind kind)
        => kind == ArenaSourceKind.Fixed
            ? new FixedMemoryArenaSource(new MemoryArena())
            : new PerDocumentArenaSource();

    private static long GetUsedBytes(MemoryArena arena)
    {
        // a zero-length rental reports the cursor of the current page.
        var cursor = arena.Rent(0);
        return ((long)(arena.RentedPageCount - 1) * JsonMemory.BufferSize) + cursor.Offset;
    }

    private sealed class SharedArenaSource(IMemoryArena arena) : IMemoryArenaSource
    {
        public IMemoryArena GetNextArena() => arena;
    }

    private sealed class PerDocumentArenaSource : IMemoryArenaSource
    {
        public List<MemoryArena> Arenas { get; } = [];

        public IMemoryArena GetNextArena()
        {
            var arena = new MemoryArena();
            Arenas.Add(arena);
            return arena;
        }
    }

    private sealed class DripStream(Stream inner, int maxBytesPerRead) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, Math.Min(count, maxBytesPerRead));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxBytesPerRead)], cancellationToken);

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public int ZeroByteReads { get; private set; }

        public int NonEmptyReads { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                ZeroByteReads++;
            }
            else
            {
                NonEmptyReads++;
            }

            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RentingStream(Stream inner, MemoryArena arena) : Stream
    {
        private const int RentLength = 16;

        public long RentedBytes { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!buffer.IsEmpty)
            {
                arena.Rent(RentLength);
                RentedBytes += RentLength;
            }

            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream(byte[] body) : Stream
    {
        private bool _delivered;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                return ValueTask.FromResult(0);
            }

            if (_delivered)
            {
                return ValueTask.FromException<int>(new IOException("The connection was reset."));
            }

            _delivered = true;
            body.CopyTo(buffer);
            return ValueTask.FromResult(body.Length);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
