using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HotChocolate.Transport.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

public partial class SinglePlanDeferTests : FusionTestBase
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_MatchThePlainTwin_When_TwoSwitchesSelectFieldsOfOtherSchemas(bool a, bool b)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "price") { price }
                    ... @defer(if: $b, label: "reviews") { reviews { body } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        run.AssertMatchesTwin();
        Assert.Equal(run.Twin.Requests, run.Deferred.Requests);
        Assert.Equal(
            (a ? 1 : 0) + (b ? 1 : 0),
            run.Deferred.Payloads.Count > 1 ? run.Deferred.PendingCount : 0);
    }

    private async Task<Gateway> CreateProductGatewayAsync()
    {
        var servers = new List<(string, TestServer)>
        {
            ("A", CreateSourceSchema(
                "A",
                """
                type Query {
                    product(id: ID!): Product @lookup
                }

                type Product @key(fields: "id") {
                    id: ID!
                    name: String!
                }
                """)),
            ("B", CreateSourceSchema(
                "B",
                """
                type Query {
                    productById(id: ID!): Product @lookup
                }

                type Product @key(fields: "id") {
                    id: ID!
                    price: Float!
                    rating: Int!
                }
                """)),
            ("C", CreateSourceSchema(
                "C",
                """
                type Query {
                    productByIdForReviews(id: ID!): Product @lookup
                }

                type Product @key(fields: "id") {
                    id: ID!
                    reviews: [Review!]!
                }

                type Review {
                    body: String!
                }
                """))
        };

        return await CreateGatewayAsync(servers);
    }

    private Task<Gateway> CreateGatewayAsync(IEnumerable<(string Name, TestServer Server)> servers)
        => CreateCompositeSchemaAsync(
            [.. servers],
            configureGatewayBuilder: builder => builder.ModifyPlannerOptions(
                options => options.EnableSinglePlanDefer = true));

    private static async Task<TwinRun> RunWithTwinAsync(
        Gateway gateway,
        string query,
        Dictionary<string, object?> variables)
    {
        var deferred = await RunAsync(gateway, query, variables);
        var twinQuery = ToPlainTwin(query);
        var twinVariables = variables
            .Where(t => twinQuery.Contains("$" + t.Key, StringComparison.Ordinal))
            .ToDictionary(t => t.Key, t => t.Value);
        var twin = await RunAsync(gateway, twinQuery, twinVariables);
        return new TwinRun(deferred, twin);
    }

    private static async Task<Run> RunAsync(
        Gateway gateway,
        string query,
        Dictionary<string, object?> variables)
    {
        var before = CountRequests(gateway);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var result = await client.PostAsync(
            new OperationRequest(query, variables: variables),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result);
        var after = CountRequests(gateway);

        var requests = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (name, count) in after)
        {
            var difference = count - before.GetValueOrDefault(name);

            if (difference > 0)
            {
                requests[name] = difference;
            }
        }

        return new Run(payloads, requests);
    }

    private static Dictionary<string, int> CountRequests(Gateway gateway)
        => gateway.Interactions.ToDictionary(
            t => t.Key,
            t => t.Value.Values.Count(i => i.Request is not null),
            StringComparer.Ordinal);

    private static async Task<List<string>> ReadPayloadsAsync(GraphQLHttpResponse response)
    {
        var body = await response.HttpResponseMessage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var payloads = new List<string>();

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = JsonNode.Parse(line)!.AsObject();
            payload.Remove("extensions");
            payloads.Add(payload.ToJsonString());
        }

        return payloads;
    }

    // Removes every @defer directive and every variable definition that has no use left.
    private static string ToPlainTwin(string query)
    {
        var plain = DeferDirective().Replace(query, string.Empty);
        var header = QueryHeader().Match(plain);

        if (!header.Success)
        {
            return plain;
        }

        var body = plain[header.Length..];
        var kept = VariableDefinition().Matches(header.Groups["variables"].Value)
            .Where(m => body.Contains("$" + m.Groups["name"].Value, StringComparison.Ordinal))
            .Select(m => m.Value.Trim().TrimEnd(','))
            .ToList();

        return kept.Count == 0
            ? "query" + body
            : "query(" + string.Join(", ", kept) + ")" + body;
    }

    [GeneratedRegex(@"\s*@defer(\([^)]*\))?")]
    private static partial Regex DeferDirective();

    [GeneratedRegex(@"query\s*\((?<variables>[^)]*)\)")]
    private static partial Regex QueryHeader();

    [GeneratedRegex(@"\$(?<name>\w+)\s*:\s*[^,$]+")]
    private static partial Regex VariableDefinition();

    private sealed record Run(List<string> Payloads, SortedDictionary<string, int> Requests)
    {
        public JsonNode Data => MergeIncrementalPayloads(Payloads);

        public string Errors => string.Join(
            "|",
            CollectErrors(Payloads).OrderBy(t => t, StringComparer.Ordinal));

        public int PendingCount => Payloads
            .Select(t => JsonNode.Parse(t)!.AsObject())
            .Sum(t => t["pending"]?.AsArray().Count ?? 0);

        public IReadOnlyList<string> Positions => CollectDeliveredPositions(Payloads);

        public string RequestSummary => string.Join(", ", Requests.Select(t => $"{t.Key}={t.Value}"));
    }

    private sealed record TwinRun(Run Deferred, Run Twin)
    {
        public void AssertMatchesTwin()
        {
            Assert.Equal(Twin.Data.ToJsonString(), Deferred.Data.ToJsonString());
            Assert.Equal(Twin.Errors, Deferred.Errors);
            Assert.Equal(Deferred.Positions.Distinct().Count(), Deferred.Positions.Count);
        }
    }

    private static IEnumerable<string> CollectErrors(List<string> payloads)
    {
        foreach (var payload in payloads.Select(t => JsonNode.Parse(t)!.AsObject()))
        {
            foreach (var error in payload["errors"]?.AsArray() ?? [])
            {
                yield return error!.ToJsonString();
            }

            foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
            {
                foreach (var error in incremental!["errors"]?.AsArray() ?? [])
                {
                    yield return error!.ToJsonString();
                }
            }

            foreach (var completed in payload["completed"]?.AsArray() ?? [])
            {
                foreach (var error in completed!["errors"]?.AsArray() ?? [])
                {
                    yield return error!.ToJsonString();
                }
            }
        }
    }

    // Every scalar leaf that a payload sends, as the path from the root of the result.
    private static List<string> CollectDeliveredPositions(List<string> payloads)
    {
        var positions = new List<string>();
        var pendingPaths = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var payload in payloads.Select(t => JsonNode.Parse(t)!.AsObject()))
        {
            if (payload["data"] is { } initial)
            {
                CollectLeaves(initial, string.Empty, positions);
            }

            foreach (var pending in payload["pending"]?.AsArray() ?? [])
            {
                pendingPaths[pending!["id"]!.GetValue<string>()] = JoinPath(pending["path"]!.AsArray());
            }

            foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
            {
                var path = pendingPaths[incremental!["id"]!.GetValue<string>()]
                    + JoinPath(incremental["subPath"]?.AsArray() ?? []);
                CollectLeaves(incremental["data"]!, path, positions);
            }
        }

        return positions;

        static string JoinPath(JsonArray segments)
            => string.Concat(segments.Select(t => "/" + t!.ToJsonString().Trim('"')));

        static void CollectLeaves(JsonNode node, string path, List<string> positions)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, value) in obj)
                    {
                        CollectLeaves(value!, path + "/" + name, positions);
                    }
                    break;

                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        CollectLeaves(array[i]!, path + "/" + i, positions);
                    }
                    break;

                default:
                    positions.Add(path);
                    break;
            }
        }
    }

    private static JsonNode MergeIncrementalPayloads(List<string> payloads)
    {
        JsonNode? data = null;
        var pendingPaths = new Dictionary<string, JsonArray>(StringComparer.Ordinal);

        foreach (var payload in payloads)
        {
            var response = JsonNode.Parse(payload)!.AsObject();

            if (response["data"] is { } initial)
            {
                data = initial.DeepClone();
            }

            foreach (var pending in response["pending"]?.AsArray() ?? [])
            {
                pendingPaths[pending!["id"]!.GetValue<string>()] = pending["path"]!.AsArray();
            }

            var entries = (response["incremental"]?.AsArray() ?? [])
                .Select(incremental => (
                    Incremental: incremental!,
                    Segments: pendingPaths[incremental!["id"]!.GetValue<string>()]
                        .Concat(incremental["subPath"]?.AsArray() ?? [])
                        .ToList()))
                .OrderBy(entry => entry.Segments.Count);

            foreach (var (incremental, segments) in entries)
            {
                var target = data!;

                foreach (var segment in segments)
                {
                    var value = segment!.GetValue<JsonElement>();
                    target = value.ValueKind == JsonValueKind.Number
                        ? target[value.GetInt32()]!
                        : target[value.GetString()!]!;
                }

                MergeInto(target.AsObject(), incremental["data"]!.AsObject());
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
            else if (value is JsonArray sourceArray && target[name] is JsonArray targetArray)
            {
                for (var i = 0; i < sourceArray.Count; i++)
                {
                    if (sourceArray[i] is JsonObject sourceItem && targetArray[i] is JsonObject targetItem)
                    {
                        MergeInto(targetItem, sourceItem);
                    }
                    else
                    {
                        targetArray[i] = sourceArray[i]?.DeepClone();
                    }
                }
            }
            else
            {
                target[name] = value?.DeepClone();
            }
        }
    }
}
