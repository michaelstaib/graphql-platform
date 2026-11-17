using System.Net;
using BenchmarkDotNet.Attributes;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Execution.Benchmarks.LookupFanOut;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Transport.Http;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotChocolate.Fusion.Execution.Benchmarks;

/// <summary>
/// Parses the subgraph response bodies of one LookupFanOut operation, once from contiguous arrays and
/// once through the HTTP response readers.
/// </summary>
[MemoryDiagnoser]
public class SourceParseBenchmark
{
    private readonly List<(byte[] Body, bool JsonLines)> _bodies = [];
    private static readonly byte[] s_tinyRecord = "{\"data\":{}}"u8.ToArray();
    private readonly List<byte[]> _records = [];

    [Params(true, false)]
    public bool ContentLength { get; set; } = true;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        const string query =
            """
            fragment User on User { id username name }
            fragment Review on Review { id body }
            fragment Product on Product { inStock name price shippingEstimate upc weight }

            query TestQuery {
              users { ...User reviews { ...Review product { ...Product reviews { ...Review author { ...User reviews { ...Review product { ...Product } } } } } } }
              topProducts { ...Product reviews { ...Review author { ...User reviews { ...Review product { ...Product } } } } }
            }
            """;

        var assembly = typeof(SourceParseBenchmark).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(t => t.EndsWith("composite-schema.graphqls"));
        await using var schemaStream = assembly.GetManifestResourceStream(resourceName)!;
        using var schemaReader = new StreamReader(schemaStream);
        var schemaDocument = Utf8GraphQLParser.Parse(await schemaReader.ReadToEndAsync());

        var services = new ServiceCollection();
        var builder = services
            .AddGraphQLGateway()
            .AddInMemoryConfiguration(schemaDocument)
            .ModifyCostOptions(o => o.EnforceCostLimits = false);

        services.RemoveAll<ISourceSchemaClientFactory>();
        services.AddSingleton<ISourceSchemaClientFactory>(new TransportSourceSchemaClientFactory(true));

        foreach (var schemaName in new[] { "accounts-api", "inventory-api", "products-api", "reviews-api" })
        {
            FusionSetupUtilities.Configure(
                builder,
                setup => setup.ClientConfigurationModifiers.Add(
                    _ => new CannedSourceSchemaClientConfiguration(schemaName)));
        }

        var executor = await services.BuildGatewayAsync();
        TransportSourceSchemaClient.Capture = (body, jsonLines) =>
        {
            lock (_bodies)
            {
                _bodies.Add((body, jsonLines));
            }
        };

        await using (await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(query).Build()))
        {
        }

        TransportSourceSchemaClient.Capture = null;

        foreach (var (body, jsonLines) in _bodies)
        {
            if (!jsonLines)
            {
                _records.Add(body);
                continue;
            }

            var start = 0;

            while (start < body.Length)
            {
                var end = Array.IndexOf(body, (byte)'\n', start);
                _records.Add(body.AsSpan(start, end - start).ToArray());
                start = end + 1;
            }
        }

        var rows = 0;
        var arena = new MemoryArena();

        foreach (var record in _records)
        {
            using var document = SourceResultDocument.Parse(arena, record, record.Length);
            rows += CountRows(document.Root);
        }

        arena.Seal();
        arena.Dispose();

        Console.WriteLine(
            $"// {_bodies.Count} bodies ({_bodies.Count(t => t.JsonLines)} JSON Lines), {_records.Count} records, "
            + $"{_records.Sum(t => t.Length)} bytes, {rows} value rows; bodies "
            + string.Join(", ", _bodies.Select(t => $"{t.Body.Length}{(t.JsonLines ? "L" : "S")}"))
            + "; records " + string.Join(", ", _records.Select(t => t.Length)));
    }

    [Benchmark(Baseline = true)]
    public int ParseArrays()
    {
        var arena = new MemoryArena();
        var count = 0;

        foreach (var record in _records)
        {
            var document = SourceResultDocument.Parse(arena, record, record.Length);
            count += document.Root.ValueKind == System.Text.Json.JsonValueKind.Object ? 1 : 0;
            document.Dispose();
        }

        arena.Seal();
        arena.Dispose();
        return count;
    }

    [Benchmark]
    public int ParseTinyRecords()
    {
        var arena = new MemoryArena();
        var count = 0;

        for (var i = 0; i < _records.Count; i++)
        {
            var document = SourceResultDocument.Parse(arena, s_tinyRecord, s_tinyRecord.Length);
            count += document.Root.ValueKind == System.Text.Json.JsonValueKind.Object ? 1 : 0;
            document.Dispose();
        }

        arena.Seal();
        arena.Dispose();
        return count;
    }

    [Benchmark]
    public int IngestJsonLinesFloor()
    {
        // The stream reads, newline scans and arena copies of the JSON Lines reader without parsing.
        var arena = new MemoryArena();
        var scratch = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
        var count = 0;

        foreach (var (body, isJsonLines) in _bodies)
        {
            if (!isJsonLines)
            {
                continue;
            }

            var stream = new TransportStream(body);
            var pending = 0;

            while (true)
            {
                var bytesRead = stream.Read(scratch.AsSpan(pending));

                if (bytesRead == 0)
                {
                    break;
                }

                var length = pending + bytesRead;
                var lastNewline = scratch.AsSpan(pending, bytesRead).LastIndexOf((byte)'\n');

                if (lastNewline < 0)
                {
                    pending = length;
                    continue;
                }

                var completeEnd = pending + lastNewline + 1;
                var block = arena.Rent(completeEnd);
                scratch.AsSpan(0, completeEnd).CopyTo(block.Span);
                pending = length - completeEnd;
                scratch.AsSpan(completeEnd, pending).CopyTo(scratch);

                var lineStart = 0;

                while (lineStart < block.Length)
                {
                    var lineEnd = lineStart + block.Span[lineStart..].IndexOf((byte)'\n');
                    count += block.Span[lineStart];
                    lineStart = lineEnd + 1;
                }
            }
        }

        System.Buffers.ArrayPool<byte>.Shared.Return(scratch);
        arena.Seal();
        arena.Dispose();
        return count;
    }

    [Benchmark]
    public Task<int> ReadTransport() => ReadAsync(singles: true, jsonLines: true);

    [Benchmark]
    public Task<int> ReadSingles() => ReadAsync(singles: true, jsonLines: false);

    [Benchmark]
    public Task<int> ReadJsonLines() => ReadAsync(singles: false, jsonLines: true);

    private async Task<int> ReadAsync(bool singles, bool jsonLines)
    {
        var arena = new MemoryArena();
        var source = new FixedSource(arena);
        var count = 0;

        foreach (var (body, isJsonLines) in _bodies)
        {
            if (isJsonLines ? !jsonLines : !singles)
            {
                continue;
            }

            if (!isJsonLines)
            {
                using var response = new GraphQLHttpResponse(
                    CreateMessage(body, "application/graphql-response+json; charset=utf-8", ContentLength));
                var document = await response.ReadAsResultAsync(arena);
                count += document.Root.ValueKind == System.Text.Json.JsonValueKind.Object ? 1 : 0;
                document.Dispose();
                continue;
            }

            using var streamResponse = new GraphQLHttpResponse(
                CreateMessage(body, "application/jsonl; charset=utf-8", false));

            await foreach (var document in streamResponse.ReadAsResultStreamAsync(source))
            {
                count += document.Root.ValueKind == System.Text.Json.JsonValueKind.Object ? 1 : 0;
                document.Dispose();
            }
        }

        arena.Seal();
        arena.Dispose();
        return count;
    }

    private static int CountRows(SourceResultElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var objectRows = 1;
                foreach (var property in element.EnumerateObject())
                {
                    objectRows += 1 + CountRows(property.Value);
                }

                return objectRows;

            case System.Text.Json.JsonValueKind.Array:
                var arrayRows = 1;
                foreach (var item in element.EnumerateArray())
                {
                    arrayRows += CountRows(item);
                }

                return arrayRows;

            default:
                return 1;
        }
    }

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

    private sealed class FixedSource(IMemoryArena arena) : IFixedMemoryArenaSource
    {
        public IMemoryArena GetNextArena() => arena;
    }
}
