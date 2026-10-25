#if FUSION
using System.Buffers;
using System.Runtime.CompilerServices;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Text.Json;
#else
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HotChocolate.Buffers;
#endif

#if FUSION
namespace HotChocolate.Fusion.Transport.Http;
#else
namespace HotChocolate.Transport.Http;
#endif

#if FUSION
internal sealed class JsonLinesReader(HttpResponseMessage message, IMemoryArenaSource arenaSource, TimeSpan readTimeout)
    : IAsyncEnumerable<SourceResultDocument>
{
    // A record of at most this many bytes is parsed as a single span, a longer record is filled into
    // the geometric data chunk schedule. An in-place read chunk holds at most this many bytes.
    private const int MaxSingleSpanRecordLength = 16 * 1024;

    // The minimum number of bytes an in-place read chunk has room for after the carried partial record.
    private const int MinInPlaceReadLength = 1024;

    // The longest partial record that is carried into the next in-place read chunk.
    private const int MaxInPlaceCarryLength = 4 * 1024;

    private const int ScratchBufferLength = 16 * 1024;

    public async IAsyncEnumerator<SourceResultDocument> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        var responseStream = await message.Content.ReadAsStreamAsync(cancellationToken);
        await using var stream = readTimeout == Timeout.InfiniteTimeSpan
            ? responseStream
            : new ReadTimeoutStream(responseStream, readTimeout);

        // Records are parsed in place from read chunks of the document arena only when all documents of
        // the stream share that arena. Otherwise, each record is copied into the arena of its document.
        var sharesArena = arenaSource is IFixedMemoryArenaSource;

        // The scratch buffer starts with the pending bytes of the partial record carried between reads.
        byte[]? scratch = null;
        var pending = 0;
        var carryInPlace = true;

        // A read chunk that could not be trimmed stays rented, and the room after its partial record,
        // which spans [heldStart, heldFilled), takes the next read.
        MemoryArena? heldArena = null;
        var held = default(MemorySegment);
        var heldStart = 0;
        var heldFilled = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var arena = sharesArena && carryInPlace ? arenaSource.GetNextArena() as MemoryArena : null;
                var useHeld = arena is not null
                    && ReferenceEquals(arena, heldArena)
                    && held.Length - heldFilled >= MinInPlaceReadLength;
                heldArena = null;

                if (!useHeld && pending > MaxInPlaceCarryLength)
                {
                    arena = null;
                }

                if (pending == 0 && scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                    scratch = null;
                }

                if (arena is not null)
                {
                    MemorySegment chunk;
                    int start;
                    int filled;

                    if (useHeld)
                    {
                        chunk = held;
                        start = heldStart;
                        filled = heldFilled;
                    }
                    else
                    {
                        // Waits for data before renting the read chunk.
                        await stream.ReadAsync(Memory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                        chunk = arena.RentUpTo(pending + MinInPlaceReadLength, MaxSingleSpanRecordLength);
                        start = 0;
                        filled = pending;
                        scratch.AsSpan(0, pending).CopyTo(chunk.Span);
                    }

                    int bytesRead;

                    try
                    {
                        bytesRead = await stream
                            .ReadAsync(chunk.Memory[filled..], cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        arena.TryTrim(chunk, start);
                        throw;
                    }

                    if (bytesRead == 0)
                    {
                        // The pending bytes are the final record, which has no line terminator.
                        if (IsWhitespaceOnly(chunk.Span[start..filled]))
                        {
                            arena.TryTrim(chunk, start);
                            yield break;
                        }

                        arena.TryTrim(chunk, filled);
                        var finalDocument = ParseRecord(
                            arenaSource.GetNextArena(),
                            arena,
                            chunk,
                            start,
                            filled - start);
                        yield return finalDocument;
                        yield break;
                    }

                    var readStart = filled;
                    filled += bytesRead;
                    var lastNewline = chunk.Span[readStart..filled].LastIndexOf((byte)'\n');
                    var completeEnd = lastNewline < 0 ? start : readStart + lastNewline + 1;
                    pending = filled - completeEnd;

                    // The partial record is copied out before the chunk is trimmed to the complete records,
                    // and before any of them rents parser memory.
                    if (pending > 0)
                    {
                        scratch ??= ArrayPool<byte>.Shared.Rent(ScratchBufferLength);
                        chunk.Span[completeEnd..filled].CopyTo(scratch);
                    }

                    if (!arena.TryTrim(chunk, completeEnd) && chunk.Length - filled >= MinInPlaceReadLength)
                    {
                        heldArena = arena;
                        held = chunk;
                        heldStart = completeEnd;
                        heldFilled = filled;
                    }
                    else if (lastNewline < 0)
                    {
                        // No record is complete yet, so the record spans reads and continues in the scratch buffer.
                        carryInPlace = false;
                    }

                    var lineStart = start;

                    while (lineStart < completeEnd)
                    {
                        var lineEnd = lineStart + chunk.Span[lineStart..completeEnd].IndexOf((byte)'\n');

                        if (!IsWhitespaceOnly(chunk.Span[lineStart..lineEnd]))
                        {
                            var document = ParseRecord(
                                arenaSource.GetNextArena(),
                                arena,
                                chunk,
                                lineStart,
                                lineEnd - lineStart);
                            yield return document;
                        }

                        lineStart = lineEnd + 1;
                    }

                    continue;
                }

                if (pending == 0)
                {
                    // A zero-byte read waits for data before the scratch buffer is rented.
                    await stream.ReadAsync(Memory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                }

                scratch ??= ArrayPool<byte>.Shared.Rent(ScratchBufferLength);

                if (pending == scratch.Length)
                {
                    var grown = ArrayPool<byte>.Shared.Rent(scratch.Length * 2);
                    scratch.AsSpan(0, pending).CopyTo(grown);
                    ArrayPool<byte>.Shared.Return(scratch);
                    scratch = grown;
                }

                var scratchBytesRead = await stream
                    .ReadAsync(scratch.AsMemory(pending), cancellationToken)
                    .ConfigureAwait(false);

                if (scratchBytesRead == 0)
                {
                    // The pending bytes are the final record, which has no line terminator.
                    if (!IsWhitespaceOnly(scratch.AsSpan(0, pending)))
                    {
                        var finalDocument = CopyAndParse(arenaSource.GetNextArena(), scratch.AsSpan(0, pending));
                        yield return finalDocument;
                    }

                    yield break;
                }

                var length = pending + scratchBytesRead;
                var recordStart = 0;
                var searchStart = pending;

                while (true)
                {
                    var newline = scratch.AsSpan(searchStart, length - searchStart).IndexOf((byte)'\n');

                    if (newline < 0)
                    {
                        break;
                    }

                    var recordEnd = searchStart + newline;

                    if (!IsWhitespaceOnly(scratch.AsSpan(recordStart, recordEnd - recordStart)))
                    {
                        var document = CopyAndParse(
                            arenaSource.GetNextArena(),
                            scratch.AsSpan(recordStart, recordEnd - recordStart));
                        yield return document;
                    }

                    recordStart = recordEnd + 1;
                    searchStart = recordStart;
                }

                pending = length - recordStart;

                if (recordStart > 0)
                {
                    scratch.AsSpan(recordStart, pending).CopyTo(scratch);
                    carryInPlace = true;
                }
            }
        }
        finally
        {
            if (scratch is not null)
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }
    }

    private static SourceResultDocument ParseRecord(
        IMemoryArena documentArena,
        MemoryArena chunkArena,
        MemorySegment chunk,
        int start,
        int length)
    {
        // A record is parsed in place only when its document is backed by the arena of the read chunk.
        if (!ReferenceEquals(documentArena, chunkArena))
        {
            return CopyAndParse(documentArena, chunk.Span.Slice(start, length));
        }

        var segments = documentArena.RentSegmentTable(1);
        segments[0] = new MemorySegment(chunk.Buffer, chunk.Offset + start, length);

        return SourceResultDocument.ParseFilled(
            documentArena,
            segments,
            usedChunks: 1,
            lastLength: length);
    }

    private static SourceResultDocument CopyAndParse(IMemoryArena arena, ReadOnlySpan<byte> record)
    {
        var length = record.Length;

        // A record whose length fits within MaxSingleSpanRecordLength is copied into a single
        // exact-length arena chunk and parsed as one span.
        if (length > 0 && length <= MaxSingleSpanRecordLength)
        {
            var buffer = arena.Rent(length);
            record.CopyTo(buffer.Span);

            var segments = arena.RentSegmentTable(1);
            segments[0] = buffer;

            return SourceResultDocument.ParseFilled(
                arena,
                segments,
                usedChunks: 1,
                lastLength: length);
        }

        // Larger records are filled across the geometric data chunk schedule and parsed as a
        // multi-segment sequence, using the same per-record mechanic as the SSE reader.
        var chunks = arena.RentSegmentTable(64);
        var chunkIndex = 0;
        var chunkSize = SourceResultDocument.GetDataChunkSize(chunkIndex);
        var current = chunks[chunkIndex] = arena.Rent(chunkSize);
        var currentChunkPosition = 0;
        var recordOffset = 0;

        while (recordOffset < length)
        {
            var spaceInCurrentChunk = chunkSize - currentChunkPosition;
            var bytesToCopy = Math.Min(spaceInCurrentChunk, length - recordOffset);

            record.Slice(recordOffset, bytesToCopy).CopyTo(current.Span[currentChunkPosition..]);
            currentChunkPosition += bytesToCopy;
            recordOffset += bytesToCopy;

            if (currentChunkPosition == chunkSize)
            {
                if (chunkIndex + 1 >= SourceResultDocument.DataMaxChunks)
                {
                    throw ThrowHelper.SourceResultDocumentTooLarge();
                }

                if (chunkIndex + 1 >= chunks.Length)
                {
                    arena.GrowSegmentTable(ref chunks);
                }

                chunkIndex++;
                chunkSize = SourceResultDocument.GetDataChunkSize(chunkIndex);
                current = chunks[chunkIndex] = arena.Rent(chunkSize);
                currentChunkPosition = 0;
            }
        }

        return SourceResultDocument.ParseFilled(
            arena,
            chunks,
            usedChunks: chunkIndex + 1,
            lastLength: currentChunkPosition);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWhitespaceOnly(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
        {
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
            {
                return false;
            }
        }
        return true;
    }
}
#else
internal class JsonLinesReader(HttpResponseMessage message) : IAsyncEnumerable<OperationResult>
{
    private static readonly StreamPipeReaderOptions s_options = new(
        pool: MemoryPool<byte>.Shared,
        bufferSize: 16 * 1024,
        minimumReadSize: 1,
        leaveOpen: true,
        useZeroByteReads: true);

    public async IAsyncEnumerator<OperationResult> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        await using var stream = await message.Content.ReadAsStreamAsync(cancellationToken);
        var reader = PipeReader.Create(stream, s_options);

        try
        {
            ReadResult result;
            do
            {
                result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (result.IsCanceled)
                {
                    yield break;
                }

                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                do
                {
                    var position = buffer.PositionOf((byte)'\n');
                    if (position is null)
                    {
                        // Mark what we've examined but not consumed
                        examined = buffer.End;
                        break;
                    }

                    var line = buffer.Slice(0, position.Value);

                    // Skip empty lines
                    if (!IsEmptyLine(line))
                    {
                        var document = ParseDocument(line);
                        yield return OperationResult.Parse(document);
                    }

                    // Move past the processed line
                    var nextPosition = buffer.GetPosition(1, position.Value);
                    consumed = nextPosition;
                    buffer = buffer.Slice(nextPosition);
                } while (!buffer.IsEmpty);

                // Tell the reader how much we've consumed and examined
                reader.AdvanceTo(consumed, examined);
            } while (!result.IsCompleted);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static JsonDocumentOwner ParseDocument(ReadOnlySequence<byte> lineBuffer)
    {
        var requiredSize = (int)lineBuffer.Length;
        var buffer = new PooledArrayWriter(requiredSize);
        lineBuffer.CopyTo(buffer.GetSpan(requiredSize));
        buffer.Advance(requiredSize);
        return new JsonDocumentOwner(JsonDocument.Parse(buffer.WrittenMemory), buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEmptyLine(ReadOnlySequence<byte> lineBuffer)
    {
        if (lineBuffer.IsEmpty)
        {
            return true;
        }

        if (lineBuffer.IsSingleSegment)
        {
            var span = lineBuffer.First.Span;
            return IsWhitespaceOnly(span);
        }

        var position = lineBuffer.Start;
        while (lineBuffer.TryGet(ref position, out var memory))
        {
            if (!IsWhitespaceOnly(memory.Span))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWhitespaceOnly(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
        {
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
            {
                return false;
            }
        }
        return true;
    }
}
#endif
