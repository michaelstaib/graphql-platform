using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Execution.Benchmarks.LookupFanOut;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Transport.Http;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Transport.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotChocolate.Fusion.Execution.Benchmarks;

/// <summary>
/// Executes the federation benchmark query like <see cref="LookupFanOutBenchmark"/>, but hands the
/// canned subgraph responses to the gateway through the HTTP response readers: batches and variable
/// sets arrive as a JSON Lines body that <see cref="GraphQLHttpResponse"/> reads into the request arena,
/// single results arrive as a graphql-response+json body. The bodies are read in reads of at most
/// <see cref="TransportStream.MaxReadSize"/> bytes. The request carries a document id, so the
/// document is parsed and validated once.
/// </summary>
[MemoryDiagnoser]
public class LookupFanOutTransportBenchmark
{
    private const string Query =
        """
        fragment User on User { id username name }
        fragment Review on Review { id body }
        fragment Product on Product { inStock name price shippingEstimate upc weight }

        query TestQuery {
          users { ...User reviews { ...Review product { ...Product reviews { ...Review author { ...User reviews { ...Review product { ...Product } } } } } } }
          topProducts { ...Product reviews { ...Review author { ...User reviews { ...Review product { ...Product } } } } }
        }
        """;

    private readonly ChunkedArrayWriter _output = new();
    private IRequestExecutor _executor = null!;
    private IOperationRequest _request = null!;

    /// <summary>
    /// Whether single results carry a Content-Length header.
    /// </summary>
    [Params(true, false)]
    public bool ContentLength { get; set; } = true;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        const string family = "composite-schema";
        var schemaDocument = Utf8GraphQLParser.Parse(ReadSchema(family));
        string[] schemaNames = ["accounts-api", "inventory-api", "products-api", "reviews-api"];

        var services = new ServiceCollection();
        var builder = services
            .AddGraphQLGateway()
            .AddInMemoryConfiguration(schemaDocument)
            .ModifyCostOptions(o => o.EnforceCostLimits = false);

        services.RemoveAll<ISourceSchemaClientFactory>();
        services.AddSingleton<ISourceSchemaClientFactory>(new TransportSourceSchemaClientFactory(ContentLength));

        foreach (var schemaName in schemaNames)
        {
            FusionSetupUtilities.Configure(
                builder,
                setup => setup.ClientConfigurationModifiers.Add(
                    _ => new CannedSourceSchemaClientConfiguration(schemaName)));
        }

        _executor = await services.BuildGatewayAsync();

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Query)));
        _request = OperationRequestBuilder.New()
            .SetDocument(Query)
            .SetDocumentId(new OperationDocumentId(hash))
            .SetDocumentHash(new OperationDocumentHash(hash, "sha256", HashFormat.Hex))
            .Build();

        TransportSourceSchemaClient.ResetCounters();
        var length = await ExecuteAsync();
        Console.WriteLine($"// transport: {TransportSourceSchemaClient.DescribeCounters()}");

        if (length < 80_000)
        {
            _output.Reset();
            await using var result = await _executor.ExecuteAsync(_request);
            throw new InvalidOperationException(
                $"Unexpected response size {length}: {result.ToJson()}");
        }

        var response = new byte[length];
        _output.CopyTo(response, 0, length);
        Console.WriteLine(
            $"// {family} transport response: {length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(response))}");
    }

    [Benchmark]
    public Task<int> ExecuteQuery() => ExecuteAsync();

    private async Task<int> ExecuteAsync()
    {
        _output.Reset();

        await using var result = await _executor.ExecuteAsync(_request);
        JsonResultFormatter.Default.Format(result.ExpectOperationResult(), _output);
        return _output.Length;
    }

    private static string ReadSchema(string family)
    {
        var assembly = typeof(LookupFanOutTransportBenchmark).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(t => t.EndsWith(family + ".graphqls", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

internal sealed class TransportSourceSchemaClientFactory(bool contentLength)
    : SourceSchemaClientFactory<CannedSourceSchemaClientConfiguration>
{
    private readonly CannedResponses _responses = new();
    private readonly JsonLinesBodies _bodies = new();

    protected override ISourceSchemaClient CreateClient(
        FusionSchemaDefinition schema,
        CannedSourceSchemaClientConfiguration configuration)
        => new TransportSourceSchemaClient(_responses, _bodies, contentLength);
}

/// <summary>
/// Caches the JSON Lines bodies of batches by the identity of their records.
/// </summary>
internal sealed class JsonLinesBodies
{
    private readonly ConcurrentDictionary<int, (byte[][] Parts, byte[] Body)> _bodies = new();

    public byte[] Get(byte[][] parts, int count)
    {
        var key = new HashCode();

        for (var i = 0; i < count; i++)
        {
            key.Add(RuntimeHelpers.GetHashCode(parts[i]));
        }

        var hash = key.ToHashCode();

        if (_bodies.TryGetValue(hash, out var entry) && Matches(entry.Parts, parts, count))
        {
            return entry.Body;
        }

        var copy = parts.AsSpan(0, count).ToArray();
        var body = new List<byte>();

        for (var i = 0; i < count; i++)
        {
            // Each record starts with batch index properties like a source schema batch record. The
            // client maps the records to the requests by their order.
            body.AddRange(Encoding.UTF8.GetBytes($"{{\"requestIndex\":0,\"variableIndex\":{i},"));
            body.AddRange(parts[i].AsSpan(1).ToArray());
            body.Add((byte)'\n');
        }

        var bytes = body.ToArray();
        _bodies[hash] = (copy, bytes);
        return bytes;
    }

    private static bool Matches(byte[][] cached, byte[][] parts, int count)
    {
        if (cached.Length != count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(cached[i], parts[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A source schema client that answers from <see cref="CannedResponses"/> and reads the response
/// bodies through <see cref="GraphQLHttpResponse"/>, like the HTTP connector.
/// </summary>
internal sealed class TransportSourceSchemaClient(
    CannedResponses responses,
    JsonLinesBodies bodies,
    bool contentLength)
    : ISourceSchemaClient
{
    private static int s_single;
    private static int s_singleBytes;
    private static int s_streams;
    private static int s_streamRecords;
    private static int s_streamBytes;

    /// <summary>
    /// Receives each response body with whether it is a JSON Lines body.
    /// </summary>
    public static Action<byte[], bool>? Capture { get; set; }

    public SourceSchemaClientCapabilities Capabilities => SourceSchemaClientCapabilities.Default;

    public static void ResetCounters()
    {
        s_single = 0;
        s_singleBytes = 0;
        s_streams = 0;
        s_streamRecords = 0;
        s_streamBytes = 0;
    }

    public static string DescribeCounters()
        => $"{s_single} single results ({s_singleBytes} bytes), "
            + $"{s_streams} JSON Lines bodies ({s_streamRecords} records, {s_streamBytes} bytes)";

    public async IAsyncEnumerable<SourceSchemaResult> ExecuteAsync(
        OperationPlanContext context,
        SourceSchemaClientRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.Variables.Length <= 1)
        {
            var bytes = responses.Get(request, request.Variables.Length == 0 ? null : request.Variables[0]);
            var path = request.Variables.Length == 0 ? CompactPath.Root : request.Variables[0].Path;
            s_single++;
            s_singleBytes += bytes.Length;
            Capture?.Invoke(bytes, false);

            using var response = new GraphQLHttpResponse(
                CreateMessage(bytes, "application/graphql-response+json; charset=utf-8", contentLength));
            var document = await response.ReadAsResultAsync(context.Memory, cancellationToken);
            yield return request.Variables.Length == 0
                || request.Variables[0].AdditionalPaths.IsDefaultOrEmpty
                ? new SourceSchemaResult(path, document)
                : new SourceSchemaResult(path, document, additionalPaths: request.Variables[0].AdditionalPaths);
            yield break;
        }

        var parts = new byte[request.Variables.Length][];

        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = responses.Get(request, request.Variables[i]);
        }

        var body = bodies.Get(parts, parts.Length);
        Capture?.Invoke(body, true);
        s_streams++;
        s_streamRecords += parts.Length;
        s_streamBytes += body.Length;

        using var streamResponse = new GraphQLHttpResponse(
            CreateMessage(body, "application/jsonl; charset=utf-8", contentLength: false));
        var index = 0;

        await foreach (var document in streamResponse
            .ReadAsResultStreamAsync(context.MemorySource)
            .WithCancellation(cancellationToken))
        {
            var variables = request.Variables[index++];
            yield return variables.AdditionalPaths.IsDefaultOrEmpty
                ? new SourceSchemaResult(variables.Path, document)
                : new SourceSchemaResult(variables.Path, document, additionalPaths: variables.AdditionalPaths);
        }
    }

    public async IAsyncEnumerable<SourceSchemaBatchResult> ExecuteBatchAsync(
        OperationPlanContext context,
        ImmutableArray<SourceSchemaClientRequest> requests,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;

        for (var i = 0; i < requests.Length; i++)
        {
            count += Math.Max(1, requests[i].Variables.Length);
        }

        var parts = new byte[count][];
        var targets = new (int RequestIndex, VariableValues? Variables)[count];
        var next = 0;

        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];

            if (request.Variables.Length == 0)
            {
                targets[next] = (i, null);
                parts[next++] = responses.Get(request, null);
                continue;
            }

            foreach (var variables in request.Variables)
            {
                targets[next] = (i, variables);
                parts[next++] = responses.Get(request, variables);
            }
        }

        var body = bodies.Get(parts, count);
        Capture?.Invoke(body, true);
        s_streams++;
        s_streamRecords += count;
        s_streamBytes += body.Length;

        using var response = new GraphQLHttpResponse(
            CreateMessage(body, "application/jsonl; charset=utf-8", contentLength: false));
        var index = 0;

        await foreach (var document in response
            .ReadAsResultStreamAsync(context.MemorySource)
            .WithCancellation(cancellationToken))
        {
            var (requestIndex, variables) = targets[index++];

            yield return new SourceSchemaBatchResult(
                requestIndex,
                variables is not { } v
                    ? new SourceSchemaResult(CompactPath.Root, document)
                    : v.AdditionalPaths.IsDefaultOrEmpty
                        ? new SourceSchemaResult(v.Path, document)
                        : new SourceSchemaResult(v.Path, document, additionalPaths: v.AdditionalPaths));
        }
    }

    public IAsyncEnumerable<SourceSchemaResult> SubscribeAsync(
        OperationPlanContext context,
        SourceSchemaClientRequest request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static HttpResponseMessage CreateMessage(byte[] body, string contentType, bool contentLength)
    {
        var content = new TransportContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);

        if (contentLength)
        {
            content.Headers.ContentLength = body.Length;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}

/// <summary>
/// The content of a canned response, read through a <see cref="TransportStream"/>.
/// </summary>
internal sealed class TransportContent(byte[] body) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => stream.WriteAsync(body).AsTask();

    protected override Task<Stream> CreateContentReadStreamAsync()
        => Task.FromResult<Stream>(new TransportStream(body));

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}

/// <summary>
/// A read-only stream over a byte array whose reads return at most <see cref="MaxReadSize"/> bytes,
/// like the connection buffer of <see cref="SocketsHttpHandler"/>.
/// </summary>
internal sealed class TransportStream(byte[] body) : Stream
{
    public const int MaxReadSize = 4096;

    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => body.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = Math.Min(Math.Min(buffer.Length, MaxReadSize), body.Length - _position);
        body.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => new(Read(buffer.Span));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
