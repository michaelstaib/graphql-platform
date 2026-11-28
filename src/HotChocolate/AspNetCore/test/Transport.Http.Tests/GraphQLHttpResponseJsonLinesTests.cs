using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace HotChocolate.Transport.Http;

public class GraphQLHttpResponseJsonLinesTests
{
    private const string First = """{"data":{"a":1}}""";
    private const string Second = """{"data":{"a":2}}""";

    [Theory]
    [InlineData(First + "\n" + Second, 1)]
    [InlineData(First + "\n" + Second, 7)]
    [InlineData(First + "\n" + Second, int.MaxValue)]
    [InlineData(First + "\r\n" + Second + "\r", 5)]
    public async Task ReadAsResultStream_Should_ReturnLastResult_When_BodyEndsWithoutLineFeed(
        string body,
        int fragmentLength)
    {
        // arrange
        using var response = CreateResponse(body, fragmentLength);

        // act
        var results = await ReadDataAsync(response);

        // assert
        Assert.Equal(["""{"a":1}""", """{"a":2}"""], results);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task ReadAsResultStream_Should_ReturnResult_When_BodyHasNoLineFeed(int fragmentLength)
    {
        // arrange
        using var response = CreateResponse(First, fragmentLength);

        // act
        var results = await ReadDataAsync(response);

        // assert
        Assert.Equal(["""{"a":1}"""], results);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r")]
    [InlineData("\n\t")]
    public async Task ReadAsResultStream_Should_ReturnNoFurtherResult_When_BodyEndsWithBlankRest(string rest)
    {
        // arrange
        using var response = CreateResponse(First + "\n" + rest, fragmentLength: 4);

        // act
        var results = await ReadDataAsync(response);

        // assert
        Assert.Equal(["""{"a":1}"""], results);
    }

    private static GraphQLHttpResponse CreateResponse(string body, int fragmentLength)
    {
        var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(body), fragmentLength))
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/jsonl");
        return new GraphQLHttpResponse(message);
    }

    private static async Task<List<string>> ReadDataAsync(GraphQLHttpResponse response)
    {
        var results = new List<string>();

        await foreach (var result in response
            .ReadAsResultStreamAsync()
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            using (result)
            {
                results.Add(result.Data.GetRawText());
            }
        }

        return results;
    }

    private sealed class FragmentedStream(byte[] payload, int fragmentLength) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var length = Math.Min(Math.Min(buffer.Length, fragmentLength), payload.Length - _position);
            payload.AsMemory(_position, length).CopyTo(buffer);
            _position += length;
            return ValueTask.FromResult(length);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
