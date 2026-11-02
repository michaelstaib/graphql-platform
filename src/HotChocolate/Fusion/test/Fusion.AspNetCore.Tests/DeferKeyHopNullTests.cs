using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public sealed class DeferKeyHopNullTests : FusionTestBase
{
    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopReturnsNullForSingleObject()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            {
                current(id: 99) {
                    user {
                        ... @defer {
                            total
                        }
                    }
                }
            }
            """);

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        var payloads = await ReadPayloadsAsync(result);
        Assert.Equal(1, KeyHopCalls.FirstHopLookups);
        Assert.Equal(0, KeyHopCalls.FinalLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"current":{"user":{}}},"pending":[{"id":"0","path":["current","user"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopReturnsNullForMiddleListItem()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            {
                users(ids: [2, 99, 3]) {
                    name
                    ... @defer {
                        total
                    }
                }
            }
            """);

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        var payloads = await ReadPayloadsAsync(result);
        Assert.Equal(3, KeyHopCalls.FirstHopLookups);
        Assert.Equal(2, KeyHopCalls.FinalLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"users":[{"name":"User 2"},{"name":"User 99"},{"name":"User 3"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":200}},{"id":"0","subPath":[1],"data":{"total":null}},{"id":"0","subPath":[2],"data":{"total":300}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopReturnsNullForLastListItem()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            {
                users(ids: [2, 3, 99]) {
                    name
                    ... @defer {
                        total
                    }
                }
            }
            """);

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        var payloads = await ReadPayloadsAsync(result);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"users":[{"name":"User 2"},{"name":"User 3"},{"name":"User 99"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":200}},{"id":"0","subPath":[1],"data":{"total":300}},{"id":"0","subPath":[2],"data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopReturnsNullForFirstListItem()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            {
                users(ids: [99, 2, 3]) {
                    name
                    ... @defer {
                        total
                    }
                }
            }
            """);

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        var payloads = await ReadPayloadsAsync(result);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"users":[{"name":"User 99"},{"name":"User 2"},{"name":"User 3"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":null}},{"id":"0","subPath":[1],"data":{"total":200}},{"id":"0","subPath":[2],"data":{"total":300}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_EveryKeyHopOfTheListReturnsNull()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            {
                users(ids: [99, 99]) {
                    name
                    ... @defer {
                        total
                    }
                }
            }
            """);

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        var payloads = await ReadPayloadsAsync(result);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"users":[{"name":"User 99"},{"name":"User 99"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":null}},{"id":"0","subPath":[1],"data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_RunMutationOnce_When_KeyHopReturnsNull()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            mutation {
                create(id: 99) {
                    user {
                        ... @defer {
                            total
                        }
                    }
                }
            }
            """);

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        var payloads = await ReadPayloadsAsync(result);
        Assert.Equal(1, KeyHopCalls.MutationCalls);
        Assert.Equal(1, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"create":{"user":{}}},"pending":[{"id":"0","path":["create","user"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact(Skip = "planner: the promoted hop node lacks the dependency on its key supplier")]
    public async Task Defer_Should_MatchNonDeferredResult_When_KeyHopReturnsValueForSingleObject()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var deferred = await RunAsync(
            client,
            "{ current(id: 2) { user { ... @defer { name total } } } }",
            KeyHopCalls.FirstHop);
        var reference = await RunAsync(
            client,
            "{ current(id: 2) { user { name total } } }",
            KeyHopCalls.FirstHop);

        // assert
        Assert.Equal(reference.Data, deferred.Data);
        Assert.Equal(reference.ErrorCount, deferred.ErrorCount);
        Assert.Equal(1, deferred.FirstHopLookups);
        deferred.Data.MatchInlineSnapshot(
            """
            {"current":{"user":{"name":"User 2","total":200}}}
            """);
    }

    [Fact(Skip = "planner: the promoted hop node lacks the dependency on its key supplier")]
    public async Task Defer_Should_MatchNonDeferredResult_When_KeyHopReturnsNullForMiddleListItemWithDeferredName()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var deferred = await RunAsync(
            client,
            "{ users(ids: [2, 99, 3]) { ... @defer { name total } } }",
            KeyHopCalls.FirstHop);
        var reference = await RunAsync(
            client,
            "{ users(ids: [2, 99, 3]) { name total } }",
            KeyHopCalls.FirstHop);

        // assert
        Assert.Equal(reference.Data, deferred.Data);
        Assert.Equal(reference.ErrorCount, deferred.ErrorCount);
        Assert.Equal(3, deferred.FirstHopLookups);
        deferred.Data.MatchInlineSnapshot(
            """
            {"users":[{"name":"User 2","total":200},{"name":"User 99","total":null},{"name":"User 3","total":300}]}
            """);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopThrowsForSingleObject()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var payloads = await PostAsync(client, "{ current(id: 98) { user { ... @defer { total } } } }");

        // assert
        Assert.Equal(1, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"current":{"user":{}}},"pending":[{"id":"0","path":["current","user"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_SecondKeyHopThrowsForMiddleListItem()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.SecondHop);
        using var gateway = await CreateGatewayAsync(twoHops: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var payloads = await PostAsync(client, "{ users(ids: [2, 98, 3]) { name ... @defer { total } } }");

        // assert
        Assert.Equal(3, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"users":[{"name":"User 2"},{"name":"User 98"},{"name":"User 3"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":200}},{"id":"0","subPath":[1],"data":{"total":null}},{"id":"0","subPath":[2],"data":{"total":300}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_FirstKeyHopThrowsForMiddleListItemOfTwoHops()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateGatewayAsync(twoHops: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var payloads = await PostAsync(client, "{ users(ids: [2, 98, 3]) { name ... @defer { total } } }");

        // assert
        Assert.Equal(3, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"users":[{"name":"User 2"},{"name":"User 98"},{"name":"User 3"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":200}},{"id":"0","subPath":[1],"data":{"total":null}},{"id":"0","subPath":[2],"data":{"total":300}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopReturnsNullInMutationOfDirectSchema()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateDirectGatewayAsync();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var payloads = await PostAsync(client, "mutation { create(id: 99) { name ... @defer { total } } }");

        // assert
        Assert.Equal(1, KeyHopCalls.MutationCalls);
        Assert.Equal(1, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"create":{"name":"User 99"}},"pending":[{"id":"0","path":["create"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopThrowsInMutationOfDirectSchema()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateDirectGatewayAsync();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var payloads = await PostAsync(client, "mutation { create(id: 98) { name ... @defer { total } } }");

        // assert
        Assert.Equal(1, KeyHopCalls.MutationCalls);
        Assert.Equal(1, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"create":{"name":"User 98"}},"pending":[{"id":"0","path":["create"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","data":{"total":null}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NullDeferredField_When_KeyHopReturnsNullInMutationListOfDirectSchema()
    {
        // arrange
        KeyHopCalls.Reset(failingHop: KeyHopCalls.FirstHop);
        using var gateway = await CreateDirectGatewayAsync();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var payloads = await PostAsync(
            client,
            "mutation { createMany(ids: [2, 99, 3]) { name ... @defer { total } } }");

        // assert
        Assert.Equal(1, KeyHopCalls.MutationCalls);
        Assert.Equal(3, KeyHopCalls.FirstHopLookups);
        payloads.MatchInlineSnapshots(
        [
            """
            {"data":{"createMany":[{"name":"User 2"},{"name":"User 99"},{"name":"User 3"}]},"pending":[{"id":"0","path":["createMany"]}],"hasNext":true}
            """,
            """
            {"incremental":[{"id":"0","subPath":[0],"data":{"total":200}},{"id":"0","subPath":[1],"data":{"total":null}},{"id":"0","subPath":[2],"data":{"total":300}}],"completed":[{"id":"0"}],"hasNext":false}
            """
        ]);
    }

    [Fact(Skip = "planner: No plan was found for the @defer fragment")]
    public async Task Defer_Should_MatchNonDeferredResult_When_KeyHopReturnsNullForObjectOfDirectSchema()
    {
        // arrange
        using var gateway = await CreateDirectGatewayAsync();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var deferred = await RunAsync(
            client,
            "{ current(id: 99) { name ... @defer { total } } }",
            KeyHopCalls.FirstHop);
        var reference = await RunAsync(
            client,
            "{ current(id: 99) { name total } }",
            KeyHopCalls.FirstHop);

        // assert
        Assert.Equal(reference.Data, deferred.Data);
        Assert.Equal(reference.ErrorCount, deferred.ErrorCount);
        Assert.Equal(1, deferred.FirstHopLookups);
        deferred.Data.MatchInlineSnapshot(
            """
            {"current":{"name":"User 99","total":null}}
            """);
    }

    [Fact(Skip = "planner: No plan was found for the @defer fragment")]
    public async Task Defer_Should_MatchNonDeferredResult_When_KeyHopReturnsNullForMiddleListItemOfDirectSchema()
    {
        // arrange
        using var gateway = await CreateDirectGatewayAsync();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        var deferred = await RunAsync(
            client,
            "{ users(ids: [2, 99, 3]) { name ... @defer { total } } }",
            KeyHopCalls.FirstHop);
        var reference = await RunAsync(
            client,
            "{ users(ids: [2, 99, 3]) { name total } }",
            KeyHopCalls.FirstHop);

        // assert
        Assert.Equal(reference.Data, deferred.Data);
        Assert.Equal(reference.ErrorCount, deferred.ErrorCount);
        Assert.Equal(3, deferred.FirstHopLookups);
        deferred.Data.MatchInlineSnapshot(
            """
            {"users":[{"name":"User 2","total":200},{"name":"User 99","total":null},{"name":"User 3","total":300}]}
            """);
    }

    [Theory]
    [MemberData(nameof(ParityCases))]
    public async Task Defer_Should_MatchNonDeferredResult_When_KeyHopReturnsNullOrThrows(
        bool twoHops,
        string failingHop,
        int key,
        string shape)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(twoHops);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, reference) = CreateOperations(shape, key);

        // act
        KeyHopCalls.Reset(failingHop);
        var deferredPayloads = await ExecuteAsync(client, deferred);
        var deferredMutationCalls = KeyHopCalls.MutationCalls;

        KeyHopCalls.Reset(failingHop);
        var referencePayloads = await ExecuteAsync(client, reference);

        // assert
        var deferredResult = MergePayloads(deferredPayloads);
        var referenceResult = MergePayloads(referencePayloads);
        Assert.Equal(referenceResult.Data, deferredResult.Data);
        Assert.Equal(referenceResult.ErrorCount, deferredResult.ErrorCount);
        Assert.Equal(shape.StartsWith("mutation", StringComparison.Ordinal) ? 1 : 0, deferredMutationCalls);
        Assert.False(deferredPayloads[^1]["hasNext"]!.GetValue<bool>());
        Assert.Equal(
            deferredPayloads.SelectMany(p => p["pending"]?.AsArray() ?? []).Select(p => p!["id"]!.ToString()),
            deferredPayloads.SelectMany(p => p["completed"]?.AsArray() ?? []).Select(p => p!["id"]!.ToString()));
    }

    public static TheoryData<bool, string, int, string> ParityCases()
    {
        var cases = new TheoryData<bool, string, int, string>();

        foreach (var (twoHops, failingHop) in new[]
        {
            (false, KeyHopCalls.FirstHop),
            (true, KeyHopCalls.FirstHop),
            (true, KeyHopCalls.SecondHop)
        })
        {
            foreach (var key in new[] { 99, 98 })
            {
                foreach (var shape in new[] { "query-object", "query-list", "mutation-object", "mutation-list" })
                {
                    cases.Add(twoHops, failingHop, key, shape);
                }
            }
        }

        return cases;
    }

    private static (string Deferred, string Reference) CreateOperations(string shape, int key)
        => shape switch
        {
            "query-object" =>
            (
                $"{{ current(id: {key}) {{ user {{ ... @defer {{ total }} }} }} }}",
                $"{{ current(id: {key}) {{ user {{ total }} }} }}"
            ),
            "query-list" =>
            (
                $"{{ users(ids: [2, {key}, 3]) {{ name ... @defer {{ total }} }} }}",
                $"{{ users(ids: [2, {key}, 3]) {{ name total }} }}"
            ),
            "mutation-object" =>
            (
                $"mutation {{ create(id: {key}) {{ user {{ ... @defer {{ total }} }} }} }}",
                $"mutation {{ create(id: {key}) {{ user {{ total }} }} }}"
            ),
            "mutation-list" =>
            (
                $"mutation {{ createMany(ids: [2, {key}, 3]) {{ name ... @defer {{ total }} }} }}",
                $"mutation {{ createMany(ids: [2, {key}, 3]) {{ name total }} }}"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

    private static async Task<List<JsonObject>> ExecuteAsync(GraphQLHttpClient client, string document)
    {
        using var result = await client.PostAsync(
            new OperationRequest(document),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        return await ParsePayloadsAsync(result);
    }

    private static async Task<List<string>> ReadPayloadsAsync(GraphQLHttpResponse response)
    {
        var payloads = await ParsePayloadsAsync(response);
        return payloads.Select(p => p.ToJsonString()).ToList();
    }

    private static async Task<List<JsonObject>> ParsePayloadsAsync(GraphQLHttpResponse response)
    {
        var body = await response.HttpResponseMessage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var payloads = new List<JsonObject>();

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = JsonNode.Parse(line)!.AsObject();
            payload.Remove("extensions");
            payloads.Add(payload);
        }

        return payloads;
    }

    private static (string? Data, int ErrorCount) MergePayloads(List<JsonObject> payloads)
    {
        var data = payloads[0]["data"]?.DeepClone();
        var errorCount = payloads[0]["errors"]?.AsArray().Count ?? 0;
        var pendingPaths = new Dictionary<string, JsonArray>();

        foreach (var payload in payloads)
        {
            foreach (var pending in payload["pending"]?.AsArray() ?? [])
            {
                pendingPaths[pending!["id"]!.ToString()] = pending["path"]!.AsArray();
            }

            foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
            {
                var target = data!;

                foreach (var segment in pendingPaths[incremental!["id"]!.ToString()]
                    .Concat(incremental["subPath"]?.AsArray() ?? []))
                {
                    target = segment!.GetValueKind() is JsonValueKind.Number
                        ? target[segment.GetValue<int>()]!
                        : target[segment.GetValue<string>()]!;
                }

                foreach (var (name, value) in incremental["data"]!.AsObject())
                {
                    target.AsObject()[name] = value?.DeepClone();
                }

                errorCount += incremental["errors"]?.AsArray().Count ?? 0;
            }

            foreach (var completed in payload["completed"]?.AsArray() ?? [])
            {
                errorCount += completed!["errors"]?.AsArray().Count ?? 0;
            }
        }

        return (data?.ToJsonString(), errorCount);
    }

    private static async Task<List<string>> PostAsync(GraphQLHttpClient client, string document)
    {
        var payloads = await ExecuteAsync(client, document);
        return payloads.Select(p => p.ToJsonString()).ToList();
    }

    private static async Task<Outcome> RunAsync(GraphQLHttpClient client, string document, string failingHop)
    {
        KeyHopCalls.Reset(failingHop);
        var merged = MergePayloads(await ExecuteAsync(client, document));
        return new Outcome(merged.Data, merged.ErrorCount, KeyHopCalls.FirstHopLookups);
    }

    private async Task<Gateway> CreateDirectGatewayAsync()
    {
        var server1 = CreateSourceSchema(
            "A",
            b => b.AddQueryType<KeyHopDirectSourceA.Query>().AddMutationType<KeyHopDirectSourceA.Mutation>());

        var server2 = CreateSourceSchema(
            "B",
            b => b.AddQueryType<KeyHopSourceB.Query>());

        var server3 = CreateSourceSchema(
            "C",
            b => b.AddQueryType<KeyHopDirectSourceC.Query>());

        return await CreateCompositeSchemaAsync(
        [
            ("A", server1),
            ("B", server2),
            ("C", server3)
        ]);
    }

    private async Task<Gateway> CreateGatewayAsync(bool twoHops)
    {
        var server1 = CreateSourceSchema(
            "A",
            b => b.AddQueryType<KeyHopSourceA.Query>().AddMutationType<KeyHopSourceA.Mutation>());

        var server2 = CreateSourceSchema(
            "B",
            b => b.AddQueryType<KeyHopSourceB.Query>());

        if (!twoHops)
        {
            var server3 = CreateSourceSchema(
                "C",
                b => b.AddQueryType<KeyHopSourceC.Query>());

            return await CreateCompositeSchemaAsync(
            [
                ("A", server1),
                ("B", server2),
                ("C", server3)
            ]);
        }

        var server3Code = CreateSourceSchema(
            "C",
            b => b.AddQueryType<KeyHopSourceCCode.Query>());

        var server4 = CreateSourceSchema(
            "D",
            b => b.AddQueryType<KeyHopSourceD.Query>());

        return await CreateCompositeSchemaAsync(
        [
            ("A", server1),
            ("B", server2),
            ("C", server3Code),
            ("D", server4)
        ]);
    }

    private static class KeyHopCalls
    {
        public const string FirstHop = "first";

        public const string SecondHop = "second";

        private static int s_mutationCalls;
        private static int s_firstHopLookups;
        private static int s_finalLookups;

        public static string FailingHop { get; private set; } = FirstHop;

        public static int MutationCalls => Volatile.Read(ref s_mutationCalls);

        public static int FirstHopLookups => Volatile.Read(ref s_firstHopLookups);

        public static int FinalLookups => Volatile.Read(ref s_finalLookups);

        public static void Reset(string failingHop)
        {
            FailingHop = failingHop;
            Volatile.Write(ref s_mutationCalls, 0);
            Volatile.Write(ref s_firstHopLookups, 0);
            Volatile.Write(ref s_finalLookups, 0);
        }

        public static void CountMutation() => Interlocked.Increment(ref s_mutationCalls);

        public static void CountFirstHopLookup() => Interlocked.Increment(ref s_firstHopLookups);

        public static void CountFinalLookup() => Interlocked.Increment(ref s_finalLookups);

        public static T? Resolve<T>(string hop, int key, Func<T> create)
            where T : class
        {
            if (FailingHop != hop)
            {
                return create();
            }

            return key switch
            {
                99 => null,
                98 => throw new GraphQLException("The key hop failed."),
                _ => create()
            };
        }
    }

    private static class KeyHopDirectSourceA
    {
        public class Query
        {
            public User GetCurrent(int id) => new(id);

            public User[] GetUsers(int[] ids) => ids.Select(id => new User(id)).ToArray();

            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public class Mutation
        {
            public User Create(int id)
            {
                KeyHopCalls.CountMutation();
                return new User(id);
            }

            public User[] CreateMany(int[] ids)
            {
                KeyHopCalls.CountMutation();
                return ids.Select(id => new User(id)).ToArray();
            }
        }

        public record User([property: ID] int Id)
        {
            public string Name => $"User {Id}";
        }
    }

    private static class KeyHopDirectSourceC
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id)
            {
                KeyHopCalls.CountFirstHopLookup();
                return KeyHopCalls.Resolve(KeyHopCalls.FirstHop, id, () => new User(id));
            }

            [Lookup]
            [Internal]
            public User? GetUserBySku(string sku) => new(int.Parse(sku[4..]));
        }

        public record User([property: ID] int Id)
        {
            [Shareable]
            public string Sku => $"sku-{Id}";
        }
    }

    private static class KeyHopSourceA
    {
        public class Query
        {
            public Holder GetCurrent(int id) => new(new User(id));

            public User[] GetUsers(int[] ids) => ids.Select(id => new User(id)).ToArray();

            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public class Mutation
        {
            public Holder Create(int id)
            {
                KeyHopCalls.CountMutation();
                return new Holder(new User(id));
            }

            public User[] CreateMany(int[] ids)
            {
                KeyHopCalls.CountMutation();
                return ids.Select(id => new User(id)).ToArray();
            }
        }

        public record Holder(User User);

        public record User([property: ID] int Id)
        {
            public string Name => $"User {Id}";
        }
    }

    private static class KeyHopSourceB
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserBySku(string sku)
            {
                KeyHopCalls.CountFinalLookup();
                return new User(sku);
            }
        }

        public record User([property: Shareable] string Sku)
        {
            public int? Total => int.Parse(Sku[4..]) * 100;
        }
    }

    private static class KeyHopSourceC
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id)
            {
                KeyHopCalls.CountFirstHopLookup();
                return KeyHopCalls.Resolve(KeyHopCalls.FirstHop, id, () => new User(id));
            }
        }

        public record User([property: ID] int Id)
        {
            [Shareable]
            public string Sku => $"sku-{Id}";
        }
    }

    private static class KeyHopSourceCCode
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id)
            {
                KeyHopCalls.CountFirstHopLookup();
                return KeyHopCalls.Resolve(KeyHopCalls.FirstHop, id, () => new User(id));
            }
        }

        public record User([property: ID] int Id)
        {
            [Shareable]
            public string Code => $"code-{Id}";
        }
    }

    private static class KeyHopSourceD
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserByCode(string code)
            {
                var id = int.Parse(code[5..]);
                return KeyHopCalls.Resolve(KeyHopCalls.SecondHop, id, () => new User(code));
            }
        }

        public record User([property: Shareable] string Code)
        {
            [Shareable]
            public string Sku => $"sku-{Code[5..]}";
        }
    }

    private sealed record Outcome(string? Data, int ErrorCount, int FirstHopLookups);
}
