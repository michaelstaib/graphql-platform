using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Transport.Http;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

public sealed class ConditionedLookupMergeTests : FusionTestBase
{
    public enum Topology
    {
        DirectKey,
        OneHopKey,
        TwoHopKey
    }

    private const string Reference =
        """
        query {
          user { id total rating }
        }
        """;

    private const string SiblingFragments =
        """
        query ($a: Boolean!, $b: Boolean!) {
          user {
            ... @include(if: $a) { total rating }
            ... @include(if: $b) { total rating }
          }
        }
        """;

    private const string NestedFragments =
        """
        query ($a: Boolean!, $b: Boolean!) {
          user {
            ... { id ... @skip(if: $b) { total rating } }
            ... @skip(if: $a) { id ... { total rating } }
          }
        }
        """;

    private const string DeferredNestedFragments =
        """
        query ($a: Boolean!, $b: Boolean!) {
          user {
            ... @defer { id ... @defer @skip(if: $b) { total rating } }
            ... @defer @skip(if: $a) { id ... @defer { total rating } }
          }
        }
        """;

    [Theory]
    [InlineData(Topology.DirectKey, false, false)]
    [InlineData(Topology.DirectKey, false, true)]
    [InlineData(Topology.DirectKey, true, false)]
    [InlineData(Topology.DirectKey, true, true)]
    [InlineData(Topology.OneHopKey, false, false)]
    [InlineData(Topology.OneHopKey, false, true)]
    [InlineData(Topology.OneHopKey, true, false)]
    [InlineData(Topology.OneHopKey, true, true)]
    [InlineData(Topology.TwoHopKey, false, false)]
    [InlineData(Topology.TwoHopKey, false, true)]
    [InlineData(Topology.TwoHopKey, true, false)]
    [InlineData(Topology.TwoHopKey, true, true)]
    public async Task Execute_Should_FetchEntityOnce_When_SiblingFragmentsDifferInConditions(
        Topology topology,
        bool a,
        bool b)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(topology);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var reference = await ExecuteAsync(gateway, client, Reference, a, b);
        var expected = a || b
            ? new JsonObject
            {
                ["user"] = new JsonObject
                {
                    ["total"] = reference.Data["user"]!["total"]!.DeepClone(),
                    ["rating"] = reference.Data["user"]!["rating"]!.DeepClone()
                }
            }
            : new JsonObject { ["user"] = new JsonObject() };

        // act
        var result = await ExecuteAsync(gateway, client, SiblingFragments, a, b);

        // assert
        Assert.Equal(expected.ToJsonString(), result.Data.ToJsonString());
        Assert.Equal(RequestsToB(topology, a, b), result.RequestCount("B"));
    }

    [Theory]
    [InlineData(Topology.DirectKey, false, false)]
    [InlineData(Topology.DirectKey, false, true)]
    [InlineData(Topology.DirectKey, true, false)]
    [InlineData(Topology.DirectKey, true, true)]
    [InlineData(Topology.OneHopKey, false, false)]
    [InlineData(Topology.OneHopKey, false, true)]
    [InlineData(Topology.OneHopKey, true, false)]
    [InlineData(Topology.OneHopKey, true, true)]
    [InlineData(Topology.TwoHopKey, false, false)]
    [InlineData(Topology.TwoHopKey, false, true)]
    [InlineData(Topology.TwoHopKey, true, false)]
    [InlineData(Topology.TwoHopKey, true, true)]
    public async Task Execute_Should_FetchEntityOnce_When_NestedFragmentsDifferInConditions(
        Topology topology,
        bool a,
        bool b)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(topology);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var expected = await ExpectedNestedFragmentsAsync(gateway, client, a, b);

        // act
        var result = await ExecuteAsync(gateway, client, NestedFragments, a, b);

        // assert
        Assert.Equal(expected.ToJsonString(), result.Data.ToJsonString());
        Assert.InRange(result.RequestCount("B"), a && b ? 0 : 1, 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_Should_FetchEntityOnce_When_DeferredNestedFragmentsDifferInConditionsOverOneKeyHop(
        bool a,
        bool b)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(Topology.OneHopKey);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var expected = await ExpectedNestedFragmentsAsync(gateway, client, a, b);

        // act
        var result = await ExecuteAsync(gateway, client, DeferredNestedFragments, a, b);

        // assert
        Assert.Equal(expected.ToJsonString(), result.Data.ToJsonString());
        Assert.Equal(1, result.RequestCount("B"));
    }

    private static int RequestsToB(Topology topology, bool a, bool b)
        => topology is Topology.DirectKey || a || b ? 1 : 0;

    private static async Task<JsonNode> ExpectedNestedFragmentsAsync(
        Gateway gateway,
        GraphQLHttpClient client,
        bool a,
        bool b)
    {
        var reference = await ExecuteAsync(gateway, client, Reference, a, b);
        var user = new JsonObject { ["id"] = reference.Data["user"]!["id"]!.DeepClone() };

        if (!(a && b))
        {
            user["total"] = reference.Data["user"]!["total"]!.DeepClone();
            user["rating"] = reference.Data["user"]!["rating"]!.DeepClone();
        }

        return new JsonObject { ["user"] = user };
    }

    private static async Task<ExecutionResult> ExecuteAsync(
        Gateway gateway,
        GraphQLHttpClient client,
        string query,
        bool a,
        bool b)
    {
        gateway.Interactions.Clear();

        var request = query.Contains("$a")
            ? new OperationRequest(
                query,
                variables: new Dictionary<string, object?> { ["a"] = a, ["b"] = b })
            : new OperationRequest(query);

        using var response = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        var body = await response.HttpResponseMessage.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        var counts = gateway.Interactions
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ToDictionary(t => t.Key, t => t.Value.Values.Sum(CountOperations));

        return new ExecutionResult(MergePayloads(body), counts);
    }

    private static int CountOperations(SourceSchemaInteraction interaction)
    {
        using var document = JsonDocument.Parse(interaction.Request!.Body.ToArray());

        return document.RootElement.ValueKind is JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 1;
    }

    private static JsonNode MergePayloads(string body)
    {
        JsonObject? data = null;
        var pendingPaths = new Dictionary<string, JsonArray>();

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = JsonNode.Parse(line)!.AsObject();
            data ??= payload["data"]!.AsObject();

            foreach (var pending in payload["pending"]?.AsArray() ?? [])
            {
                pendingPaths[pending!["id"]!.GetValue<string>()] = pending["path"]!.AsArray();
            }

            foreach (var item in payload["incremental"]?.AsArray() ?? [])
            {
                var target = (JsonNode)data;
                var path = pendingPaths[item!["id"]!.GetValue<string>()]
                    .Concat(item["subPath"]?.AsArray() ?? []);

                foreach (var segment in path)
                {
                    target = segment!.GetValueKind() is JsonValueKind.Number
                        ? target[segment.GetValue<int>()]!
                        : target[segment.GetValue<string>()]!;
                }

                MergeInto(target.AsObject(), item["data"]!.AsObject());
            }
        }

        return data!;
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source.ToArray())
        {
            if (value is JsonObject sourceObject && target[name] is JsonObject targetObject)
            {
                MergeInto(targetObject, sourceObject);
            }
            else
            {
                target[name] = value?.DeepClone();
            }
        }
    }

    private async Task<Gateway> CreateGatewayAsync(Topology topology)
    {
        var userApi = CreateSourceSchema(
            "A",
            """
            type Query {
              user: User
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """);

        if (topology is Topology.DirectKey)
        {
            var totalById = CreateSourceSchema(
                "B",
                """
                type Query {
                  userById(id: ID!): User @lookup @internal
                }

                type User @key(fields: "id") {
                  id: ID!
                  total: Int!
                  rating: Int!
                }
                """);

            return await CreateCompositeSchemaAsync([("A", userApi), ("B", totalById)]);
        }

        var totalBySku = CreateSourceSchema(
            "B",
            """
            type Query {
              userBySku(sku: String!): User @lookup @internal
            }

            type User @key(fields: "sku") {
              sku: String! @shareable
              total: Int!
              rating: Int!
            }
            """);

        if (topology is Topology.OneHopKey)
        {
            var skuById = CreateSourceSchema(
                "C",
                """
                type Query {
                  userById(id: ID!): User @lookup @internal
                }

                type User @key(fields: "id") {
                  id: ID!
                  sku: String! @shareable
                }
                """);

            return await CreateCompositeSchemaAsync([("A", userApi), ("B", totalBySku), ("C", skuById)]);
        }

        var codeById = CreateSourceSchema(
            "C",
            """
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              code: String! @shareable
            }
            """);

        var skuByCode = CreateSourceSchema(
            "D",
            """
            type Query {
              userByCode(code: String!): User @lookup @internal
            }

            type User @key(fields: "code") {
              code: String! @shareable
              sku: String! @shareable
            }
            """);

        return await CreateCompositeSchemaAsync(
            [("A", userApi), ("B", totalBySku), ("C", codeById), ("D", skuByCode)]);
    }

    private sealed record ExecutionResult(JsonNode Data, Dictionary<string, int> Counts)
    {
        public int RequestCount(string schemaName)
            => Counts.GetValueOrDefault(schemaName);
    }
}
