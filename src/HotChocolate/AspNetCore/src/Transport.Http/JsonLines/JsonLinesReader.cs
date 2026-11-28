#if FUSION
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
internal sealed class JsonLinesReader(
    HttpResponseMessage message,
    IMemoryArenaSource arenaSource,
    TimeSpan readTimeout,
    SourceResultShapeSelector shapes)
    : IAsyncEnumerable<SourceResultDocument>
{
    // A record of at most this many bytes is parsed as a single span, a longer record is filled into
    // the geometric data chunk schedule. The complete records of one read are copied into a single
    // arena chunk when they span at most this many bytes.
    private const int MaxSingleSpanRecordLength = 16 * 1024;

    private const int ScratchBufferLength = 16 * 1024;

    // A body that declares at most this many bytes is read whole into a single arena chunk.
    private const int MaxDeclaredBodyLength = JsonMemory.BufferSize;

    // The arena chunks of a stream whose documents share one arena are kept unparsed until the body
    // has ended or they hold more than this many bytes.
    private const int MaxKeptRecordsLength = JsonMemory.BufferSize;

    private const int InitialKeptChunkCount = 16;

    public async IAsyncEnumerator<SourceResultDocument> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        var responseStream = await message.Content.ReadAsStreamAsync(cancellationToken);
        await using var stream = readTimeout == Timeout.InfiniteTimeSpan
            ? responseStream
            : new ReadTimeoutStream(responseStream, readTimeout);

        // When all documents of the stream share one arena, the complete records of a read are copied
        // into one arena chunk, and the chunks are parsed in place when the body has ended. Otherwise,
        // each record is copied into the arena of its document.
        var sharesArena = arenaSource is IFixedMemoryArenaSource;

        // The scratch buffer starts with the pending bytes of the partial record carried between reads.
        byte[]? scratch = null;
        var pending = 0;

        // The arena chunks whose records are not parsed yet.
        IMemoryArena? keptArena = null;
        MemorySegment[]? kept = null;
        var keptCount = 0;
        var keptLength = 0;
        ExceptionDispatchInfo? readFailure = null;

        try
        {
            var declaredLength = sharesArena ? message.Content.Headers.ContentLength : null;

            if (declaredLength is > 0 and <= MaxDeclaredBodyLength)
            {
                // The body is read into one arena chunk of the declared length, and its complete records
                // are parsed in place.
                var arena = arenaSource.GetNextArena();
                var block = arena.Rent((int)declaredLength.Value);
                var filled = 0;

                while (filled < block.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bytesRead = await stream
                        .ReadAsync(block.Memory[filled..], cancellationToken)
                        .ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    filled += bytesRead;
                }

                var completeEnd = block.Span[..filled].LastIndexOf((byte)'\n') + 1;
                var lineStart = 0;

                while (lineStart < completeEnd)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var lineEnd = lineStart + block.Span[lineStart..completeEnd].IndexOf((byte)'\n');
                    var lineLength = lineEnd - lineStart;

                    if (!IsWhitespaceOnly(block.Span.Slice(lineStart, lineLength)))
                    {
                        // The chunk is at most one arena page, which is the longest span a document
                        // reads as a single data chunk, so a record of any length is parsed in place.
                        var document = ParseInPlace(arena, block, lineStart, lineLength, shapes);
                        yield return document;
                    }

                    lineStart = lineEnd + 1;
                }

                // The bytes after the last line terminator continue in the scratch buffer, as do the
                // bytes of a body that is longer than it declared.
                pending = filled - completeEnd;

                if (pending > 0)
                {
                    scratch = ArrayPool<byte>.Shared.Rent(Math.Max(ScratchBufferLength, pending));
                    block.Span.Slice(completeEnd, pending).CopyTo(scratch);
                }
            }

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

                int bytesRead;

                try
                {
                    bytesRead = await stream
                        .ReadAsync(scratch.AsMemory(pending), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (keptCount > 0 && ex is not OperationCanceledException)
                {
                    // The kept records were read before the failure, so they are parsed before it is thrown.
                    readFailure = ExceptionDispatchInfo.Capture(ex);
                    bytesRead = 0;
                }

                var length = pending + bytesRead;
                var completeEnd = 0;

                if (bytesRead > 0)
                {
                    var lastNewline = scratch.AsSpan(pending, bytesRead).LastIndexOf((byte)'\n');

                    if (lastNewline < 0)
                    {
                        // No record is complete yet, so the record continues in the scratch buffer.
                        pending = length;
                        continue;
                    }

                    completeEnd = pending + lastNewline + 1;
                }

                var keepsRecords = sharesArena && bytesRead > 0 && completeEnd <= MaxSingleSpanRecordLength;

                if (keptCount > 0 && (!keepsRecords || keptLength + completeEnd > MaxKeptRecordsLength))
                {
                    // The kept chunks hold the records that precede the records of this read.
                    for (var i = 0; i < keptCount; i++)
                    {
                        var block = kept![i];
                        var lineStart = 0;

                        while (lineStart < block.Length)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var lineEnd = lineStart + block.Span[lineStart..].IndexOf((byte)'\n');

                            if (!IsWhitespaceOnly(block.Span[lineStart..lineEnd]))
                            {
                                var document = ParseInPlace(
                                    keptArena!,
                                    block,
                                    lineStart,
                                    lineEnd - lineStart,
                                    shapes);
                                yield return document;
                            }

                            lineStart = lineEnd + 1;
                        }
                    }

                    keptCount = 0;
                    keptLength = 0;
                }

                readFailure?.Throw();

                if (bytesRead == 0)
                {
                    // The pending bytes are the final record, which has no line terminator.
                    if (!IsWhitespaceOnly(scratch.AsSpan(0, pending)))
                    {
                        var finalDocument = CopyAndParse(
                            arenaSource.GetNextArena(),
                            scratch.AsSpan(0, pending),
                            shapes);
                        yield return finalDocument;
                    }

                    yield break;
                }

                if (keepsRecords)
                {
                    var blockStart = SkipBlankLines(scratch.AsSpan(0, completeEnd));

                    if (blockStart < completeEnd)
                    {
                        keptArena ??= arenaSource.GetNextArena();
                        var block = keptArena.Rent(completeEnd - blockStart);
                        scratch.AsSpan(blockStart, completeEnd - blockStart).CopyTo(block.Span);

                        if (kept is null)
                        {
                            kept = keptArena.RentSegmentTable(InitialKeptChunkCount);
                        }
                        else if (keptCount == kept.Length)
                        {
                            keptArena.GrowSegmentTable(ref kept);
                        }

                        kept[keptCount++] = block;
                        keptLength += block.Length;
                    }

                    pending = length - completeEnd;
                    scratch.AsSpan(completeEnd, pending).CopyTo(scratch);
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
                            scratch.AsSpan(recordStart, recordEnd - recordStart),
                            shapes);
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

    private static SourceResultDocument ParseInPlace(
        IMemoryArena arena,
        MemorySegment block,
        int start,
        int length,
        SourceResultShapeSelector shapes)
    {
        var segments = arena.RentSegmentTable(1);
        var record = segments[0] = new MemorySegment(block.Buffer, block.Offset + start, length);

        return SourceResultDocument.ParseFilled(
            arena,
            segments,
            usedChunks: 1,
            lastLength: length,
            shapes.GetShape(record.Span));
    }

    private static SourceResultDocument CopyAndParse(
        IMemoryArena arena,
        ReadOnlySpan<byte> record,
        SourceResultShapeSelector shapes)
    {
        var length = record.Length;
        var shape = shapes.GetShape(record);

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
                lastLength: length,
                shape);
        }

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
            lastLength: currentChunkPosition,
            shape);
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
