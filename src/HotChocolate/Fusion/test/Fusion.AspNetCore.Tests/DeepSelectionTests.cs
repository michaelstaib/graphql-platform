using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HotChocolate.Fusion;

public class DeepSelectionTests : FusionTestBase
{
    private static readonly JsonDocumentOptions s_documentOptions = new() { MaxDepth = 512 };
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        MaxDepth = 512,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    [Theory]
    [InlineData(33)]
    [InlineData(34)]
    public async Task Execute_Should_ReturnSourceSchemaResult_When_SelectionIsDeep(int depth)
    {
        // arrange
        var sourceSchemaResult = CreateSourceSchemaResult(depth);
        using var server = CreateSourceSchema(
            "A",
            CreateSourceSchemaText(depth),
            mockHttpResponse: _ => CreateResponse(sourceSchemaResult));
        using var gateway = await CreateCompositeSchemaAsync([("A", server)], includeOperationPlan: false);

        // act
        var lines = await PostAsync(gateway.CreateClient(), "{ " + CreateSelection(depth) + " }", "application/json");

        // assert
        var result = Assert.Single(lines);
        Assert.Equal(Serialize(sourceSchemaResult["data"]), Serialize(result["data"]));
        Assert.Equal(Serialize(sourceSchemaResult["errors"]), Serialize(result["errors"]));
    }

    [Theory]
    [InlineData(33)]
    [InlineData(34)]
    public async Task Execute_Should_DeliverSourceSchemaResult_When_DeferredSelectionIsDeep(int depth)
    {
        // arrange
        var sourceSchemaResult = CreateSourceSchemaResult(depth);
        using var server = CreateSourceSchema(
            "A",
            CreateSourceSchemaText(depth),
            mockHttpResponse: _ => CreateResponse(sourceSchemaResult));
        using var gateway = await CreateCompositeSchemaAsync([("A", server)], includeOperationPlan: false);

        // act
        var lines = await PostAsync(
            gateway.CreateClient(),
            "{ ... @defer { " + CreateSelection(depth) + " } }",
            "application/jsonl");

        // assert
        var incremental = Assert.Single(lines.SelectMany(GetIncremental));
        Assert.Equal(Serialize(sourceSchemaResult["data"]), Serialize(incremental["data"]));
        Assert.Equal(Serialize(sourceSchemaResult["errors"]), Serialize(incremental["errors"]));
    }

    private static string CreateSourceSchemaText(int depth)
    {
        var text = new StringBuilder("type Query { root: L0 }\n");

        for (var i = 0; i < depth; i++)
        {
            text.Append($"type L{i} {{ next: L{i + 1} }}\n");
        }

        text.Append($"type L{depth} {{ name: String }}\n");
        return text.ToString();
    }

    private static string CreateSelection(int depth)
    {
        var selection = "name";

        for (var i = 0; i < depth; i++)
        {
            selection = "next { " + selection + " }";
        }

        return "root { " + selection + " }";
    }

    private static JsonObject CreateSourceSchemaResult(int depth)
    {
        JsonNode value = new JsonObject { ["name"] = null };
        var path = new JsonArray("root");

        for (var i = 0; i < depth; i++)
        {
            value = new JsonObject { ["next"] = value };
            path.Add("next");
        }

        path.Add("name");

        return new JsonObject
        {
            ["data"] = new JsonObject { ["root"] = value },
            ["errors"] = new JsonArray(new JsonObject { ["message"] = "Name failed.", ["path"] = path })
        };
    }

    private static Task<HttpResponseMessage> CreateResponse(JsonObject result)
        => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Serialize(result),
                    Encoding.UTF8,
                    new MediaTypeHeaderValue("application/json"))
            });

    private static IEnumerable<JsonNode> GetIncremental(JsonNode line)
        => line["incremental"]?.AsArray().Select(t => t!) ?? [];

    private static string Serialize(JsonNode? node)
        => node?.ToJsonString(s_serializerOptions) ?? "null";

    private static async Task<List<JsonNode>> PostAsync(HttpClient client, string query, string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://localhost:5000/graphql"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query }),
            Encoding.UTF8,
            "application/json");
        request.Headers.Add("Accept", accept);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line, documentOptions: s_documentOptions)!)
            .ToList();
    }
}
