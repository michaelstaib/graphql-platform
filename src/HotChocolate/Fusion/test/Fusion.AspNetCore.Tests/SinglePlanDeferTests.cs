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
        var gateway = await CreateProductGatewayAsync();

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
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_MatchThePlainTwin_When_PriceLivesOnTheParentSchema(bool a, bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! price: Float! }
                """),
            ("C", ReviewsSource));

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
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_SendNameOnce_When_AFieldIsPlainUnderAConditionAndDeferred(bool x, bool d)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! @shareable }
                """),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! @shareable price: Float! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") { name price }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x, ["d"] = d });

        // assert
        MatchRun(run, $"x={x}, d={d}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_MatchThePlainTwin_When_SwitchesAreNested(bool a, bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "outer") {
                        price
                        ... @defer(if: $b, label: "inner") { reviews { body } }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Fact]
    public async Task Defer_Should_MatchThePlainTwin_When_TheAnchorIsInsideAList()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { products: [Product!]! product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! }
                """),
            ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name ... @defer(label: "price") { price } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_MatchThePlainTwin_When_DefersAreNestedWithoutASwitch()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "outer") {
                        price
                        ... @defer(label: "inner") { reviews { body } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendASharedFieldOnce_When_SiblingGroupsSelectIt()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    ... @defer(label: "a") { price rating }
                    ... @defer(label: "b") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, mergedOnly: true);
    }

    [Fact]
    public async Task Defer_Should_SendTheFieldOnceWithTwoPendingIds_When_UnlabelledSiblingsSelectTheSameField()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { price }
                    ... @defer { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheFieldOnce_When_ALabelledAndAnUnlabelledSiblingSelectTheSameField()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { price }
                    ... @defer(label: "labelled") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, mergedOnly: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_AnnounceOnlyTheOuterGroup_When_TheInnerSwitchIsConditional(bool c)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($c: Boolean!) {
                product(id: "1") {
                    ... @defer(label: "o") {
                        ... @defer(if: $c, label: "c") { price }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["c"] = c });

        // assert
        MatchRun(run, $"c={c}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition(bool x)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { user(id: ID!): User @lookup }
                type User @key(fields: "id") { id: ID! friends: [User!]! }
                """),
            ("E", """
                type Query { userById(id: ID!): User @lookup }
                type User @key(fields: "id") { id: ID! name: String! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                user(id: "1") {
                    friends @include(if: $x) { id }
                    ... @defer(label: "o") { friends { ... @defer(label: "c") { name } } }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        MatchRun(run, $"x={x}", compareValues: false);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_SkipTheLookupOfTheSwitchThatIsOn_When_SiblingSwitchesSelectTheSameFields(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "a") { price }
                    ... @defer(if: $b, label: "b") { price }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}", mergedOnly: true);
    }

    [Fact]
    public async Task Defer_Should_SendTheSharedFieldOnlyInTheInitialPayload_When_OnlyTheOtherSwitchIsOn()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "a") { price rating }
                    ... @defer(if: $b, label: "b") { price }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = true, ["b"] = false });

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_StartTheNestedLookupBeforeTheOuterLookupAnswers_When_TheKeyIsKnown()
    {
        // arrange
        var ratingReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpMessageInvoker? priceInvoker = null;
        HttpMessageInvoker? ratingInvoker = null;
        var servers = new List<(string, TestServer)>
        {
            ("A", CreateSourceSchema("A", ProductSource)),
            ("B", CreateSourceSchema(
                "B",
                """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! }
                """,
                mockHttpResponse: async request =>
                {
                    // the response for the price is held back until the request for the rating is received.
                    await ratingReceived.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    return await priceInvoker!.SendAsync(request, CancellationToken.None);
                })),
            ("C", CreateSourceSchema(
                "C",
                """
                type Query { productByIdForRating(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! rating: Int! }
                """,
                mockHttpResponse: request =>
                {
                    ratingReceived.TrySetResult();
                    return ratingInvoker!.SendAsync(request, CancellationToken.None);
                }))
        };
        priceInvoker = new HttpMessageInvoker(servers[1].Item2.CreateHandler());
        ratingInvoker = new HttpMessageInvoker(servers[2].Item2.CreateHandler());
        var gateway = await CreateGatewayAsync(servers);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        price
                        ... @defer(label: "i") { rating }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.True(ratingReceived.Task.IsCompletedSuccessfully);
        Assert.True(run.Deferred.IndexOfPending("i") >= run.Deferred.IndexOfCompleted("o"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Defer_Should_DeliverOnlyTheFieldsLeft_When_AFieldIsSelectedInAnInitialAndADeferredSwitch(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "a") { price rating }
                    ... @defer(if: $b, label: "b") { price }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Fact]
    public async Task Defer_Should_SendTypenameInTheInitialPayload_When_TheGroupSelectsOnlyTypename()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "t") { __typename } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnTheBytesOfThePlainPath_When_TheOperationHasNoDefer()
    {
        // arrange
        var sources = new[]
        {
            ("A", ProductSource),
            ("B", PriceSource)
        };
        var withOption = await CreateGatewayAsync(sources);
        var withoutOption = await CreateCompositeSchemaAsync(
            [.. sources.Select(t => (t.Item1, CreateSourceSchema(t.Item1, t.Item2)))]);

        // act
        var onBody = await ReadBodyAsync(withOption, "{ product(id: \"1\") { name price rating } }");
        var offBody = await ReadBodyAsync(withoutOption, "{ product(id: \"1\") { name price rating } }");

        // assert
        Assert.Equal(offBody, onBody);
    }

    private const string ProductSource =
        """
        type Query { product(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string PriceSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! price: Float! rating: Int! }
        """;

    private const string ReviewsSource =
        """
        type Query { productByIdForReviews(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! reviews: [Review!]! }
        type Review { body: String! }
        """;

    private Task<Gateway> CreateProductGatewayAsync()
        => CreateGatewayAsync(("A", ProductSource), ("B", PriceSource), ("C", ReviewsSource));

    private Task<Gateway> CreateGatewayAsync(params (string Name, string Sdl)[] sources)
        => CreateGatewayAsync(
            sources.Select(t => (t.Name, CreateSourceSchema(t.Name, t.Sdl))).ToList());

    private Task<Gateway> CreateGatewayAsync(IEnumerable<(string Name, TestServer Server)> servers)
        => CreateCompositeSchemaAsync(
            [.. servers],
            configureGatewayBuilder: builder => builder.ModifyPlannerOptions(
                options => options.EnableSinglePlanDefer = true));

    private static async Task<string> ReadBodyAsync(Gateway gateway, string query)
    {
        using var client = gateway.CreateClient();
        using var content = new StringContent(
            new JsonObject { ["query"] = query }.ToJsonString(),
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            "http://localhost:5000/graphql",
            content,
            TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static void MatchRun(
        TwinRun run,
        string? postFix = null,
        bool withPayloads = false,
        bool mergedOnly = false,
        bool compareValues = true)
    {
        run.AssertMatchesTwin(compareValues);

        var snapshot = Snapshot.Create(postFix);

        if (withPayloads)
        {
            for (var i = 0; i < run.Deferred.Payloads.Count; i++)
            {
                snapshot.Add(ToShape(run.Deferred.Payloads[i]), $"Payload {i + 1}", "json");
            }
        }
        else if (mergedOnly)
        {
            snapshot.Add(ToShape(run.Deferred.Data).ToJsonString(), "Merged", "json");
        }
        else
        {
            snapshot.Add(ToDelivery(run.Deferred), "Delivery", "text");
        }

        snapshot.Add(
            $"deferred: {run.Deferred.RequestSummary}" + Environment.NewLine
            + $"plain twin: {run.Twin.RequestSummary}",
            "Requests",
            "text");
        snapshot.MatchMarkdownSnapshot();
    }

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
        public JsonNode Data
        {
            get
            {
                try
                {
                    return MergeIncrementalPayloads(Payloads);
                }
                catch (NullReferenceException ex)
                {
                    throw new InvalidOperationException(
                        "The payloads cannot be merged: " + string.Join(Environment.NewLine, Payloads),
                        ex);
                }
            }
        }

        public string Errors => string.Join(
            "|",
            CollectErrors(Payloads).OrderBy(t => t, StringComparer.Ordinal));

        public int IndexOfPending(string label)
            => Payloads.FindIndex(
                t => JsonNode.Parse(t)!["pending"]?.AsArray().Any(p => p!["label"]?.GetValue<string>() == label) is true);

        public int IndexOfCompleted(string label)
        {
            var ids = new Dictionary<string, string>();

            for (var i = 0; i < Payloads.Count; i++)
            {
                var payload = JsonNode.Parse(Payloads[i])!.AsObject();

                foreach (var pending in payload["pending"]?.AsArray() ?? [])
                {
                    ids[pending!["id"]!.GetValue<string>()] = pending["label"]?.GetValue<string>() ?? string.Empty;
                }

                foreach (var completed in payload["completed"]?.AsArray() ?? [])
                {
                    if (ids[completed!["id"]!.GetValue<string>()] == label)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        public int PendingCount => Payloads
            .Select(t => JsonNode.Parse(t)!.AsObject())
            .Sum(t => t["pending"]?.AsArray().Count ?? 0);

        public IReadOnlyList<string> Positions => CollectDeliveredPositions(Payloads);

        public string RequestSummary => string.Join(", ", Requests.Select(t => $"{t.Key}={t.Value}"));
    }

    private sealed record TwinRun(Run Deferred, Run Twin)
    {
        public void AssertMatchesTwin(bool compareValues)
        {
            var twinData = compareValues ? Twin.Data : ToShape(Twin.Data);
            var deferredData = compareValues ? Deferred.Data : ToShape(Deferred.Data);

            Assert.True(
                Canonical(twinData) == Canonical(deferredData),
                "Expected " + Twin.Data.ToJsonString() + Environment.NewLine
                + "Actual   " + string.Join(Environment.NewLine, Deferred.Payloads));
            Assert.Equal(Twin.Errors, Deferred.Errors);
            Assert.Equal(Deferred.Positions.Distinct().Count(), Deferred.Positions.Count);
        }
    }

    // The payload without the values of the scalars, which the mocked source schemas generate.
    private static string ToShape(string payload)
    {
        var node = JsonNode.Parse(payload)!.AsObject();

        if (node["data"] is { } data)
        {
            node["data"] = ToShape(data);
        }

        foreach (var incremental in node["incremental"]?.AsArray() ?? [])
        {
            incremental!["data"] = ToShape(incremental["data"]!);
        }

        return node.ToJsonString();
    }

    private static JsonNode ToShape(JsonNode node)
        => node switch
        {
            JsonObject obj => new JsonObject(obj.Select(t => KeyValuePair.Create<string, JsonNode?>(t.Key, ToShape(t.Value!)))),
            JsonArray array => new JsonArray(array.Select(t => ToShape(t!)).ToArray()),
            JsonValue value when value.GetValueKind() is JsonValueKind.String => JsonValue.Create("s")!,
            JsonValue value when value.GetValueKind() is JsonValueKind.Number => JsonValue.Create(0)!,
            _ => node.DeepClone()
        };

    // The delivery of a run without the split into payloads, which depends on the order in which
    // the source schemas answer.
    private static string ToDelivery(Run run)
    {
        var lines = new List<string>();
        var groups = new Dictionary<string, string>();

        foreach (var payload in run.Payloads.Select(t => JsonNode.Parse(t)!.AsObject()))
        {
            if (payload["data"] is { } data)
            {
                lines.Add("initial " + ToShape(data).ToJsonString());
            }

            foreach (var pending in payload["pending"]?.AsArray() ?? [])
            {
                var label = pending!["label"]?.GetValue<string>() ?? "<unlabelled " + pending["id"] + ">";
                groups[pending["id"]!.GetValue<string>()] = label;
                lines.Add($"pending {label} at {pending["path"]!.ToJsonString()}");
            }

            foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
            {
                var label = groups[incremental!["id"]!.GetValue<string>()];
                var subPath = incremental!["subPath"]?.ToJsonString() ?? "[]";
                lines.Add($"incremental {label} subPath {subPath} {ToShape(incremental["data"]!).ToJsonString()}");
            }

            foreach (var completed in payload["completed"]?.AsArray() ?? [])
            {
                lines.Add("completed " + groups[completed!["id"]!.GetValue<string>()]);
            }
        }

        var initial = lines.Where(t => t.StartsWith("initial", StringComparison.Ordinal));
        var rest = lines.Where(t => !t.StartsWith("initial", StringComparison.Ordinal)).OrderBy(t => t, StringComparer.Ordinal);
        return string.Join(Environment.NewLine, initial.Concat(rest));
    }

    // The order of the properties of a merged result depends on the order of the payloads.
    private static string Canonical(JsonNode node)
        => node switch
        {
            JsonObject obj => "{"
                + string.Join(
                    ",",
                    obj.OrderBy(t => t.Key, StringComparer.Ordinal)
                        .Select(t => "\"" + t.Key + "\":" + Canonical(t.Value!)))
                + "}",
            JsonArray array => "[" + string.Join(",", array.Select(t => Canonical(t!))) + "]",
            _ => node.ToJsonString()
        };

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
