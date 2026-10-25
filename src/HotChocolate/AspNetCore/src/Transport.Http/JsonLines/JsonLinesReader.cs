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
    // the geometric data chunk schedule. The complete records of one read are copied into a single
    // arena chunk when they span at most this many bytes.
    private const int MaxSingleSpanRecordLength = 16 * 1024;

    private const int ScratchBufferLength = 16 * 1024;

    public async IAsyncEnumerator<SourceResultDocument> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        var responseStream = await message.Content.ReadAsStreamAsync(cancellationToken);
        await using var stream = readTimeout == Timeout.InfiniteTimeSpan
            ? responseStream
            : new ReadTimeoutStream(responseStream, readTimeout);

        // When all documents of the stream share one arena, the complete records of a read are copied
        // into one arena chunk and parsed in place. Otherwise, each record is copied into the arena of
        // its document.
        var sharesArena = arenaSource is IFixedMemoryArenaSource;

        // The scratch buffer starts with the pending bytes of the partial record carried between reads.
        byte[]? scratch = null;
        var pending = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A per-document stream holds no scratch buffer while it waits for data, and a scratch
                // buffer grown for a long record is given back once that record is complete.
                if (pending == 0
                    && scratch is not null
                    && (!sharesArena || scratch.Length > ScratchBufferLength))
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                    scratch = null;
                }

                if (pending == 0 && !sharesArena)
                {
                    // A per-document stream waits for data with a zero-byte read before it rents the scratch buffer.
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

                var bytesRead = await stream
                    .ReadAsync(scratch.AsMemory(pending), cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    // The pending bytes are the final record, which has no line terminator.
                    if (!IsWhitespaceOnly(scratch.AsSpan(0, pending)))
                    {
                        var finalDocument = CopyAndParse(arenaSource.GetNextArena(), scratch.AsSpan(0, pending));
                        yield return finalDocument;
                    }

                    yield break;
                }

                var length = pending + bytesRead;
                var lastNewline = scratch.AsSpan(pending, bytesRead).LastIndexOf((byte)'\n');

                if (lastNewline < 0)
                {
                    // No record is complete yet, so the record continues in the scratch buffer.
                    pending = length;
                    continue;
                }

                var completeEnd = pending + lastNewline + 1;

                if (sharesArena && completeEnd <= MaxSingleSpanRecordLength)
                {
                    var blockStart = SkipBlankLines(scratch.AsSpan(0, completeEnd));
                    var arena = arenaSource.GetNextArena();
                    var block = default(MemorySegment);

                    if (blockStart < completeEnd)
                    {
                        block = arena.Rent(completeEnd - blockStart);
                        scratch.AsSpan(blockStart, completeEnd - blockStart).CopyTo(block.Span);
                    }

                    pending = length - completeEnd;
                    scratch.AsSpan(completeEnd, pending).CopyTo(scratch);

                    var lineStart = 0;

                    while (lineStart < block.Length)
                    {
                        var lineEnd = lineStart + block.Span[lineStart..].IndexOf((byte)'\n');

                        if (!IsWhitespaceOnly(block.Span[lineStart..lineEnd]))
                        {
                            var document = ParseInPlace(arena, block, lineStart, lineEnd - lineStart);
                            yield return document;
                        }

                        lineStart = lineEnd + 1;
                    }

                    continue;
                }

                var recordStart = 0;
                var searchStart = pending;

                while (recordStart < completeEnd)
                {
                    var recordEnd = searchStart
                        + scratch.AsSpan(searchStart, completeEnd - searchStart).IndexOf((byte)'\n');

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

                pending = length - completeEnd;
                scratch.AsSpan(completeEnd, pending).CopyTo(scratch);
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

    private static SourceResultDocument ParseInPlace(IMemoryArena arena, MemorySegment block, int start, int length)
    {
        var segments = arena.RentSegmentTable(1);
        segments[0] = new MemorySegment(block.Buffer, block.Offset + start, length);

        return SourceResultDocument.ParseFilled(
            arena,
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

    // Returns the start of the first line that is not whitespace-only, or the length of the span when
    // every line is whitespace-only.
    private static int SkipBlankLines(ReadOnlySpan<byte> span)
    {
        var lineStart = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var b = span[i];

            if (b == (byte)'\n')
            {
                lineStart = i + 1;
            }
            else if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
            {
                return lineStart;
            }
        }

        return span.Length;
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
