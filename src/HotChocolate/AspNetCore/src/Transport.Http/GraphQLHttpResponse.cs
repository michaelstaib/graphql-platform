#if FUSION
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Text.Json;
#else
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
#endif

#if FUSION
namespace HotChocolate.Fusion.Transport.Http;
#else
namespace HotChocolate.Transport.Http;
#endif

/// <summary>
/// Represents a GraphQL over HTTP response.
/// </summary>
public sealed class GraphQLHttpResponse : IDisposable
{
#if FUSION
    private const string ContentTypeHeaderName = "Content-Type";
    private const string CharsetPrefix = "charset=";
    private const string Utf8 = "utf-8";
    private const string JsonUtf8ContentType = $"{ContentType.Json}; charset={Utf8}";
    private const string GraphQLUtf8ContentType = $"{ContentType.GraphQL}; charset={Utf8}";
    private const string EventStreamUtf8ContentType = $"{ContentType.EventStream}; charset={Utf8}";
    private const string GraphQLJsonLineUtf8ContentType = $"{ContentType.GraphQLJsonLine}; charset={Utf8}";
    private const string JsonLineUtf8ContentType = $"{ContentType.JsonLine}; charset={Utf8}";

    // A result that declares fewer bytes than this is read whole into a single arena chunk.
    private const int MaxSingleChunkResponseLength = JsonMemory.BufferSize;
#endif
    private readonly HttpResponseMessage _message;

    /// <summary>
    /// Initializes a new instance of <see cref="GraphQLHttpResponse"/>.
    /// </summary>
    /// <param name="message">
    /// The underlying HTTP response message.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    public GraphQLHttpResponse(HttpResponseMessage message)
    {
        _message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// Gets the underlying <see cref="HttpResponseMessage"/>.
    /// </summary>
    public HttpResponseMessage HttpResponseMessage => _message;

    /// <summary>
    /// Gets the HTTP response version.
    /// </summary>
    public Version Version => _message.Version;

    /// <summary>
    /// Gets the HTTP response status code.
    /// </summary>
    public HttpStatusCode StatusCode => _message.StatusCode;

    /// <summary>
    /// Specifies whether the HTTP response was successful.
    /// </summary>
    public bool IsSuccessStatusCode => _message.IsSuccessStatusCode;

    /// <summary>
    /// Gets the reason phrase which typically is sent by servers together with the status code.
    /// </summary>
    public string? ReasonPhrase => _message.ReasonPhrase;

    /// <summary>
    /// Throws an exception if the HTTP response was unsuccessful.
    /// </summary>
    public void EnsureSuccessStatusCode() => _message.EnsureSuccessStatusCode();

    /// <summary>
    /// Gets the collection of HTTP response headers.
    /// </summary>
    /// <returns>
    /// The collection of HTTP response headers.
    /// </returns>
    public HttpResponseHeaders Headers => _message.Headers;

    /// <summary>
    /// Gets the HTTP content headers as defined in RFC 2616.
    /// </summary>
    /// <returns>
    /// The content headers as defined in RFC 2616.
    /// </returns>
    public HttpContentHeaders ContentHeaders => _message.Content.Headers;

#if FUSION
    /// <summary>
    /// Gets the raw Content-Type header value without parsing into <see cref="MediaTypeHeaderValue"/>.
    /// </summary>
    public string? RawContentType
    {
        get
        {
            if (_message.Content.Headers.NonValidated.TryGetValues(ContentTypeHeaderName, out var values))
            {
                var enumerator = values.GetEnumerator();
                if (!enumerator.MoveNext())
                {
                    return null;
                }

                var mediaType = enumerator.Current;

                // Some handlers may emit media type and charset as separate values.
                // Normalize known UTF-8 combinations back to shared constants.
                if (enumerator.MoveNext()
                    && TryNormalizeKnownUtf8ContentType(mediaType.AsSpan(), enumerator.Current.AsSpan(), out var normalized))
                {
                    return normalized;
                }

                return mediaType;
            }

            return null;
        }
    }

    private static bool TryNormalizeKnownUtf8ContentType(
        ReadOnlySpan<char> mediaType,
        ReadOnlySpan<char> charset,
        out string contentType)
    {
        if (!IsUtf8(charset))
        {
            contentType = null!;
            return false;
        }

        mediaType = NormalizeMediaType(mediaType);

        if (mediaType.Equals(ContentType.GraphQL, StringComparison.OrdinalIgnoreCase))
        {
            contentType = GraphQLUtf8ContentType;
            return true;
        }

        if (mediaType.Equals(ContentType.JsonLine, StringComparison.OrdinalIgnoreCase))
        {
            contentType = JsonLineUtf8ContentType;
            return true;
        }

        if (mediaType.Equals(ContentType.Json, StringComparison.OrdinalIgnoreCase))
        {
            contentType = JsonUtf8ContentType;
            return true;
        }

        if (mediaType.Equals(ContentType.EventStream, StringComparison.OrdinalIgnoreCase))
        {
            contentType = EventStreamUtf8ContentType;
            return true;
        }

        if (mediaType.Equals(ContentType.GraphQLJsonLine, StringComparison.OrdinalIgnoreCase))
        {
            contentType = GraphQLJsonLineUtf8ContentType;
            return true;
        }

        contentType = null!;
        return false;
    }

    private static bool IsUtf8(ReadOnlySpan<char> value)
    {
        value = TrimWhiteSpace(value);

        if (value.Length > 0 && value[0] == ';')
        {
            value = TrimWhiteSpace(value[1..]);
        }

        if (value.StartsWith(CharsetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = TrimWhiteSpace(value[CharsetPrefix.Length..]);
        }

        if (value.Length > 1 && value[0] == '"' && value[^1] == '"')
        {
            value = TrimWhiteSpace(value[1..^1]);
        }

        return value.Equals(Utf8, StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlySpan<char> NormalizeMediaType(ReadOnlySpan<char> mediaType)
    {
        mediaType = TrimWhiteSpace(mediaType);

        if (mediaType.Length > 0 && mediaType[^1] == ';')
        {
            mediaType = TrimWhiteSpace(mediaType[..^1]);
        }

        return mediaType;
    }

    private static ReadOnlySpan<char> TrimWhiteSpace(ReadOnlySpan<char> value)
    {
        var start = 0;
        var end = value.Length - 1;

        while (start <= end && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end >= start && char.IsWhiteSpace(value[end]))
        {
            end--;
        }

        return value[start..(end + 1)];
    }

    /// <summary>
    /// Extracts the media type and charset from the raw Content-Type header
    /// without allocating a <see cref="MediaTypeHeaderValue"/>.
    /// </summary>
    private bool TryGetRawMediaTypeAndCharSet(
        out ReadOnlySpan<char> mediaType,
        out string? charSet)
    {
        if (!_message.Content.Headers.NonValidated.TryGetValues(ContentTypeHeaderName, out var values))
        {
            mediaType = default;
            charSet = null;
            return false;
        }

        var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            mediaType = default;
            charSet = null;
            return false;
        }

        var rawValue = enumerator.Current.AsSpan();

        // Some handlers may emit media type and charset as separate values.
        if (enumerator.MoveNext())
        {
            mediaType = NormalizeMediaType(rawValue);
            var charsetValue = enumerator.Current.AsSpan();
            charSet = IsUtf8(charsetValue) ? Utf8 : charsetValue.Trim().ToString();
            return true;
        }

        // Single header value — split on ';' to separate media type from parameters.
        var semicolonIndex = rawValue.IndexOf(';');
        if (semicolonIndex < 0)
        {
            mediaType = TrimWhiteSpace(rawValue);
            charSet = null;
            return true;
        }

        mediaType = TrimWhiteSpace(rawValue[..semicolonIndex]);
        var parameters = rawValue[(semicolonIndex + 1)..];

        // Extract charset from parameters (e.g., " charset=utf-8").
        var charsetIndex = parameters.IndexOf(CharsetPrefix, StringComparison.OrdinalIgnoreCase);
        if (charsetIndex >= 0)
        {
            var charsetSpan = TrimWhiteSpace(parameters[(charsetIndex + CharsetPrefix.Length)..]);

            // Strip quotes if present.
            if (charsetSpan.Length > 1 && charsetSpan[0] == '"' && charsetSpan[^1] == '"')
            {
                charsetSpan = charsetSpan[1..^1];
            }

            charSet = charsetSpan.Equals(Utf8, StringComparison.OrdinalIgnoreCase) ? Utf8 : charsetSpan.ToString();
        }
        else
        {
            charSet = null;
        }

        return true;
    }
#endif

    /// <summary>
    /// Gets the collection of trailing headers included in an HTTP response.
    /// </summary>
    /// <exception cref="T:System.Net.Http.HttpRequestException">
    /// PROTOCOL_ERROR: The HTTP/2 response contains pseudo-headers in the Trailing Headers Frame.
    /// </exception>
    /// <returns>
    /// The collection of trailing headers in the HTTP response.
    /// </returns>
    public HttpResponseHeaders TrailingHeaders => _message.TrailingHeaders;

#if FUSION
    /// <summary>
    /// Reads the GraphQL response as a <see cref="SourceResultDocument"/>.
    /// </summary>
    /// <param name="arena">
    /// The memory arena that backs the document produced from the response payload.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the HTTP request.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that represents the asynchronous read operation
    /// to read the <see cref="SourceResultDocument"/> from the underlying <see cref="HttpResponseMessage"/>.
    /// </returns>
    public ValueTask<SourceResultDocument> ReadAsResultAsync(
        IMemoryArena arena,
        CancellationToken cancellationToken = default)
        => ReadAsResultAsync(arena, shape: null, cancellationToken);

    /// <summary>
    /// Reads the GraphQL response as a <see cref="SourceResultDocument"/> and records which of
    /// its objects have the shape that <paramref name="shape"/> expects of them.
    /// </summary>
    /// <param name="arena">
    /// The memory arena that backs the document produced from the response payload.
    /// </param>
    /// <param name="shape">
    /// The shape that the response is expected to have, or <c>null</c> when nothing is expected.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the HTTP request.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that represents the asynchronous read operation
    /// to read the <see cref="SourceResultDocument"/> from the underlying <see cref="HttpResponseMessage"/>.
    /// </returns>
    internal ValueTask<SourceResultDocument> ReadAsResultAsync(
        IMemoryArena arena,
        SourceResultShape? shape,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arena);

        if (!TryGetRawMediaTypeAndCharSet(out var mediaType, out var charSet))
        {
            // A caller cancellation can tear down the response before its content
            // type is available. Report that as a cancellation rather than the
            // misleading "unexpected content type" error so the execution node can
            // treat it as an intentional abort.
            cancellationToken.ThrowIfCancellationRequested();
            _message.EnsureSuccessStatusCode();
            throw new InvalidOperationException("Received a successful response with an unexpected content type.");
        }

        // The server supports the newer graphql-response+json media type, and users are free
        // to use status codes.
        if (mediaType.Equals(ContentType.GraphQL, StringComparison.OrdinalIgnoreCase))
        {
            return ReadAsResultInternalAsync(arena, shape, charSet, cancellationToken);
        }

        // The server supports the older application/json media type, and the status code
        // is expected to be a 2xx for a valid GraphQL response.
        if (mediaType.Equals(ContentType.Json, StringComparison.OrdinalIgnoreCase))
        {
            _message.EnsureSuccessStatusCode();
            return ReadAsResultInternalAsync(arena, shape, charSet, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _message.EnsureSuccessStatusCode();

        throw new InvalidOperationException("Received a successful response with an unexpected content type.");
    }
#else
    /// <summary>
    /// Reads the GraphQL response as a <see cref="OperationResult"/>.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the HTTP request.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that represents the asynchronous read operation
    /// to read the <see cref="OperationResult"/> from the underlying <see cref="HttpResponseMessage"/>.
    /// </returns>
    public ValueTask<OperationResult> ReadAsResultAsync(CancellationToken cancellationToken = default)
    {
        var contentType = _message.Content.Headers.ContentType;

        // The server supports the newer graphql-response+json media type, and users are free
        // to use status codes.
        if (contentType?.MediaType?.Equals(ContentType.GraphQL, StringComparison.Ordinal) ?? false)
        {
            return ReadAsResultInternalAsync(contentType.CharSet, cancellationToken);
        }

        // The server supports the older application/json media type, and the status code
        // is expected to be a 2xx for a valid GraphQL response.
        if (contentType?.MediaType?.Equals(ContentType.Json, StringComparison.Ordinal) ?? false)
        {
            _message.EnsureSuccessStatusCode();
            return ReadAsResultInternalAsync(contentType.CharSet, cancellationToken);
        }

        _message.EnsureSuccessStatusCode();

        throw new InvalidOperationException("Received a successful response with an unexpected content type.");
    }
#endif

#if FUSION
    private async ValueTask<SourceResultDocument> ReadAsResultInternalAsync(
        IMemoryArena arena,
        SourceResultShape? shape,
        string? charSet,
        CancellationToken ct)
#else
    private async ValueTask<OperationResult> ReadAsResultInternalAsync(string? charSet, CancellationToken ct)
#endif
    {
        await using var contentStream = await _message.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var stream = contentStream;

        var sourceEncoding = HttpTransportUtilities.GetEncoding(charSet);
        if (HttpTransportUtilities.NeedsTranscoding(sourceEncoding))
        {
            stream = HttpTransportUtilities.GetTranscodingStream(contentStream, sourceEncoding);
        }

#if FUSION
        var contentLength = _message.Content.Headers.ContentLength;

        if (contentLength is not (> 0 and < MaxSingleChunkResponseLength))
        {
            // A body that declares its length ends without a further wait, so only a body without a
            // declared length is completed at the end of its root value.
            return await ReadIntoArenaAsync(
                stream,
                arena,
                shape,
                completeAtRootEnd: _message.Version.Major == 1 && contentLength is null,
                prefix: default,
                ct).ConfigureAwait(false);
        }

        // The chunk has one byte more than the body declares, so the read that finds the end of the
        // body also finds a body that is longer than it declared.
        var chunk = arena.Rent((int)contentLength.Value + 1);
        var position = 0;

        while (position < chunk.Length)
        {
            ct.ThrowIfCancellationRequested();

            var bytesRead = await stream.ReadAsync(chunk.Memory[position..], ct).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                var segments = arena.RentSegmentTable(1);
                segments[0] = new MemorySegment(chunk.Buffer, chunk.Offset, position);

                return SourceResultDocument.ParseFilled(
                    arena,
                    segments,
                    usedChunks: 1,
                    lastLength: position,
                    shape);
            }

            position += bytesRead;
        }

        // The body is longer than it declared, so it continues in the data chunks of a document of
        // any length.
        return await ReadIntoArenaAsync(
            stream,
            arena,
            shape,
            completeAtRootEnd: false,
            chunk.Memory,
            ct).ConfigureAwait(false);
#else
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        try
        {
            return OperationResult.Parse(document);
        }
        catch
        {
            document.Dispose();
            throw;
        }
#endif
    }

#if FUSION
    private static async ValueTask<SourceResultDocument> ReadIntoArenaAsync(
        Stream stream,
        IMemoryArena arena,
        SourceResultShape? shape,
        bool completeAtRootEnd,
        ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken)
    {
        Debug.Assert(prefix.Length <= JsonMemory.BufferSize);
        Debug.Assert(prefix.IsEmpty || !completeAtRootEnd);

        var chunks = arena.RentSegmentTable(64);
        var chunkIndex = 0;
        var chunkSize = SourceResultDocument.GetDataChunkSize(chunkIndex);
        var current = chunks[chunkIndex] = arena.Rent(chunkSize);
        var position = 0;
        var scanner = default(JsonRootEndScanner);
        var rootEnded = false;

        // The prefix holds the bytes of the body that were read before. It is at most one arena page
        // long, which the first chunks of the segment table hold.
        while (!prefix.IsEmpty)
        {
            var bytesToCopy = Math.Min(chunkSize - position, prefix.Length);
            prefix.Span[..bytesToCopy].CopyTo(current.Span[position..]);
            prefix = prefix[bytesToCopy..];
            position += bytesToCopy;

            if (position == chunkSize)
            {
                chunkIndex++;
                chunkSize = SourceResultDocument.GetDataChunkSize(chunkIndex);
                current = chunks[chunkIndex] = arena.Rent(chunkSize);
                position = 0;
            }
        }

        // With completeAtRootEnd (HTTP/1.x without a declared length), the read stops once the root value has
        // ended and only whitespace followed it in the same read, and disposing the stream drains the rest of
        // the body. Otherwise the read continues to the end of the body.
        while (!rootEnded)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await stream
                .ReadAsync(current.Memory[position..], cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                break;
            }

            if (completeAtRootEnd)
            {
                var received = current.Span.Slice(position, bytesRead);
                var rootEnd = scanner.Scan(received);

                if (rootEnd >= 0)
                {
                    rootEnded = JsonRootEndScanner.IsWhitespace(received[rootEnd..]);
                }
            }

            position += bytesRead;

            if (position == chunkSize && !rootEnded)
            {
                if (chunkIndex + 1 >= SourceResultDocument.DataMaxChunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var probe = arena.Rent(1);

                    if (await stream.ReadAsync(probe.Memory, cancellationToken).ConfigureAwait(false) != 0)
                    {
                        throw ThrowHelper.SourceResultDocumentTooLarge();
                    }

                    break;
                }

                if (chunkIndex + 1 >= chunks.Length)
                {
                    arena.GrowSegmentTable(ref chunks);
                }

                chunkIndex++;
                chunkSize = SourceResultDocument.GetDataChunkSize(chunkIndex);
                current = chunks[chunkIndex] = arena.Rent(chunkSize);
                position = 0;
            }
        }

        return SourceResultDocument.ParseFilled(arena, chunks, chunkIndex + 1, position, shape);
    }

    /// <summary>
    /// Reads the GraphQL response as a <see cref="IAsyncEnumerable{T}"/> of <see cref="SourceResultDocument"/>.
    /// </summary>
    /// <param name="arenaSource">The source of arenas that back the produced documents.</param>
    /// <param name="requireStreaming">
    /// When <c>true</c>, a response that is not delivered over a streaming transport (Server-Sent Events
    /// or JSON Lines) is rejected. A subscription requires a streaming response.
    /// </param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> of <see cref="SourceResultDocument"/> that represents the asynchronous
    /// read operation to read the stream of <see cref="SourceResultDocument"/>s from the underlying
    /// <see cref="HttpResponseMessage"/>.
    /// </returns>
    public IAsyncEnumerable<SourceResultDocument> ReadAsResultStreamAsync(
        IMemoryArenaSource arenaSource,
        bool requireStreaming = false)
        => ReadAsResultStreamAsync(arenaSource, requireStreaming, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Reads the GraphQL response as a <see cref="IAsyncEnumerable{T}"/> of <see cref="SourceResultDocument"/>.
    /// </summary>
    /// <param name="arenaSource">The source of arenas that back the produced documents.</param>
    /// <param name="requireStreaming">
    /// When <c>true</c>, a response that is not delivered over a streaming transport (Server-Sent Events
    /// or JSON Lines) is rejected. A subscription requires a streaming response.
    /// </param>
    /// <param name="readTimeout">
    /// The maximum time between two reads of data from a streaming response (Server-Sent Events or
    /// JSON Lines). Keep-alive messages count as data. <see cref="Timeout.InfiniteTimeSpan"/> disables
    /// the timeout. A finite value must be between one millisecond and <c>uint.MaxValue - 1</c>
    /// milliseconds. Non-streaming responses are not affected.
    /// </param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> of <see cref="SourceResultDocument"/> that represents the asynchronous
    /// read operation to read the stream of <see cref="SourceResultDocument"/>s from the underlying
    /// <see cref="HttpResponseMessage"/>.
    /// </returns>
    public IAsyncEnumerable<SourceResultDocument> ReadAsResultStreamAsync(
        IMemoryArenaSource arenaSource,
        bool requireStreaming,
        TimeSpan readTimeout)
    {
        ArgumentNullException.ThrowIfNull(arenaSource);

        if (readTimeout != Timeout.InfiniteTimeSpan && !ReadTimeoutStream.IsValidTimeout(readTimeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(readTimeout),
                readTimeout,
                "The read timeout must be Timeout.InfiniteTimeSpan or between 1 millisecond "
                + "and uint.MaxValue - 1 milliseconds.");
        }

        if (!TryGetRawMediaTypeAndCharSet(out var mediaType, out var charSet))
        {
            _message.EnsureSuccessStatusCode();
            throw new InvalidOperationException("Received a successful response with an unexpected content type.");
        }

        if (mediaType.Equals(ContentType.EventStream, StringComparison.OrdinalIgnoreCase))
        {
            return new SseReader(_message, arenaSource, readTimeout);
        }

        if (mediaType.Equals(ContentType.GraphQLJsonLine, StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(ContentType.JsonLine, StringComparison.OrdinalIgnoreCase))
        {
            return new JsonLinesReader(_message, arenaSource, readTimeout);
        }

        if (requireStreaming)
        {
            // A subscription must be answered with a streaming transport so that every event is backed
            // by its own arena. A single JSON body would yield multiple documents sharing one arena.
            throw new GraphQLHttpStreamException(
                "A subscription requires a streaming response (Server-Sent Events or JSON Lines).");
        }

        // The server supports the newer graphql-response+json media type, and users are free
        // to use status codes.
        if (mediaType.Equals(ContentType.GraphQL, StringComparison.OrdinalIgnoreCase))
        {
            return new GraphQLHttpSingleResultEnumerable(
                ct => ReadAsResultInternalAsync(arenaSource.GetNextArena(), shape: null, charSet, ct));
        }

        _message.EnsureSuccessStatusCode();

        // The server supports the older application/json media type, and the status code
        // is expected to be a 2xx for a valid GraphQL response.
        if (mediaType.Equals(ContentType.Json, StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResultEnumerable(_message, arenaSource, charSet);
        }

        throw new InvalidOperationException("Received a successful response with an unexpected content type.");
    }
#else
    /// <summary>
    /// Reads the GraphQL response as a <see cref="IAsyncEnumerable{T}"/> of <see cref="OperationResult"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> of <see cref="OperationResult"/> that represents the asynchronous
    /// read operation to read the stream of <see cref="OperationResult"/>s from the underlying
    /// <see cref="HttpResponseMessage"/>.
    /// </returns>
    public IAsyncEnumerable<OperationResult> ReadAsResultStreamAsync()
    {
        var contentType = _message.Content.Headers.ContentType;

        if (contentType?.MediaType?.Equals(ContentType.EventStream, StringComparison.Ordinal) ?? false)
        {
            return new SseReader(_message);
        }

        if ((contentType?.MediaType?.Equals(ContentType.GraphQLJsonLine, StringComparison.Ordinal) ?? false)
            || (contentType?.MediaType?.Equals(ContentType.JsonLine, StringComparison.Ordinal) ?? false))
        {
            return new JsonLinesReader(_message);
        }

        // The server supports the newer graphql-response+json media type, and users are free
        // to use status codes.
        if (contentType?.MediaType?.Equals(ContentType.GraphQL, StringComparison.Ordinal) ?? false)
        {
            return new GraphQLHttpSingleResultEnumerable(
                ct => ReadAsResultInternalAsync(contentType.CharSet, ct));
        }

        // The server supports the older application/json media type, and the status code
        // is expected to be a 2xx for a valid GraphQL response.
        if (contentType?.MediaType?.Equals(ContentType.Json, StringComparison.Ordinal) ?? false)
        {
            _message.EnsureSuccessStatusCode();

            return new JsonResultEnumerable(_message, contentType.CharSet);
        }

        _message.EnsureSuccessStatusCode();

        throw new InvalidOperationException("Received a successful response with an unexpected content type.");
    }
#endif

    /// <summary>
    /// Disposes the underlying <see cref="HttpResponseMessage"/>.
    /// </summary>
    public void Dispose() => _message.Dispose();
}
