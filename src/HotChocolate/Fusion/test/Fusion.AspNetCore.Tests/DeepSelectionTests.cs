using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;

namespace HotChocolate.Fusion;

public class DeepSelectionTests : FusionTestBase
{
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
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest("{ " + CreateSelection(depth) + " }");

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, postFix: depth.ToString());
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
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest("{ ... @defer { " + CreateSelection(depth) + " } }");

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        var body = await result.HttpResponseMessage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, postFix: depth.ToString(), stableStream: true);
        GetEnvelopes(body).MatchInlineSnapshots(
            [
                """
                {"pending":[{"id":"0","path":[]}],"hasNext":true}
                """,
                """
                {"incremental":[{"id":"0"}],"completed":[{"id":"0"}],"hasNext":false}
                """
            ]);
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

    private static List<string> GetEnvelopes(string body)
    {
        var envelopes = new List<string>();

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var envelope = JsonNode.Parse(line)!.AsObject();
            envelope.Remove("data");
            envelope.Remove("errors");

            foreach (var item in envelope["incremental"]?.AsArray() ?? [])
            {
                var incremental = item!.AsObject();
                incremental.Remove("data");
                incremental.Remove("errors");
            }

            envelopes.Add(envelope.ToJsonString());
        }

        return envelopes;
    }

    private static string Serialize(JsonNode? node)
        => node?.ToJsonString() ?? "null";
}
