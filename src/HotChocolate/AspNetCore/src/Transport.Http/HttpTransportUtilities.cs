using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

#if FUSION
namespace HotChocolate.Fusion.Transport.Http;
#else
namespace HotChocolate.Transport.Http;
#endif

internal static class HttpTransportUtilities
{
    private static readonly Encoding s_utf8 = Encoding.UTF8;

#if FUSION
    private static readonly Type? s_connectionResponseContentType =
        Type.GetType("System.Net.Http.HttpConnectionResponseContent, System.Net.Http", throwOnError: false);
#endif

    public static Encoding? GetEncoding(string? charset)
    {
        Encoding? encoding = null;

        if (charset != null)
        {
            try
            {
                // Remove at most a single set of quotes.
                if (charset.Length > 2 && charset[0] == '\"' && charset[^1] == '\"')
                {
                    encoding = Encoding.GetEncoding(charset[1..^1]);
                }
                else
                {
                    encoding = Encoding.GetEncoding(charset);
                }
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException("Invalid Charset", e);
            }

            Debug.Assert(encoding != null);
        }

        return encoding;
    }

    public static bool NeedsTranscoding([NotNullWhen(true)] Encoding? sourceEncoding)
        => sourceEncoding is not null
            && !Equals(sourceEncoding.EncodingName, s_utf8.EncodingName);

    public static Stream GetTranscodingStream(Stream contentStream, Encoding sourceEncoding)
        => Encoding.CreateTranscodingStream(
            contentStream,
            innerStreamEncoding: sourceEncoding,
            outerStreamEncoding: s_utf8);

#if FUSION
    /// <summary>
    /// Gets the stream that reads <paramref name="content"/>. The stream of a response that
    /// <see cref="SocketsHttpHandler"/> received is taken synchronously, any other content is read
    /// with <see cref="HttpContent.ReadAsStreamAsync(CancellationToken)"/>.
    /// </summary>
    public static ValueTask<Stream> ReadAsStreamAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (TryReadAsStream(content, cancellationToken, out var stream))
        {
            return new ValueTask<Stream>(stream);
        }

        return new ValueTask<Stream>(content.ReadAsStreamAsync(cancellationToken));
    }

    /// <summary>
    /// Gets the stream of a response that <see cref="SocketsHttpHandler"/> received without waiting.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="stream"/> was taken; <see langword="false"/> when
    /// <paramref name="content"/> must be read asynchronously.
    /// </returns>
    public static bool TryReadAsStream(
        HttpContent content,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out Stream? stream)
    {
        // The exact type check keeps contents whose synchronous read would block on the async path.
        if (content.GetType() == s_connectionResponseContentType)
        {
            stream = content.ReadAsStream(cancellationToken);
            return true;
        }

        stream = null;
        return false;
    }
#endif
}
