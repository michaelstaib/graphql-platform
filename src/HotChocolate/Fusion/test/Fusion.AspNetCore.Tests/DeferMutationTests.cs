using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

public class DeferMutationTests : FusionTestBase
{
    [Theory]
    [InlineData(Topology.DirectKey, MutationShape.Nested)]
    [InlineData(Topology.DirectKey, MutationShape.NestedTotalFirst)]
    [InlineData(Topology.DirectKey, MutationShape.Sibling)]
    [InlineData(Topology.DirectKey, MutationShape.NamedFragments)]
    [InlineData(Topology.DirectKey, MutationShape.IfVariableTrue)]
    [InlineData(Topology.DirectKey, MutationShape.IfVariableFalse)]
    [InlineData(Topology.DirectKey, MutationShape.InnerIfVariableTrue)]
    [InlineData(Topology.DirectKey, MutationShape.TwoRootFields)]
    [InlineData(Topology.DirectKey, MutationShape.ListField)]
    [InlineData(Topology.OneHopKey, MutationShape.Nested)]
    [InlineData(Topology.OneHopKey, MutationShape.Sibling)]
    [InlineData(Topology.OneHopKey, MutationShape.NamedFragments)]
    [InlineData(Topology.OneHopKey, MutationShape.IfVariableTrue)]
    [InlineData(Topology.OneHopKey, MutationShape.IfVariableFalse)]
    [InlineData(Topology.OneHopKey, MutationShape.InnerIfVariableTrue)]
    [InlineData(Topology.OneHopKey, MutationShape.TwoRootFields)]
    [InlineData(Topology.OneHopKey, MutationShape.ListField)]
    [InlineData(Topology.TwoHopKey, MutationShape.Nested)]
    [InlineData(Topology.TwoHopKey, MutationShape.Sibling)]
    [InlineData(Topology.TwoHopKey, MutationShape.NamedFragments)]
    [InlineData(Topology.TwoHopKey, MutationShape.IfVariableTrue)]
    [InlineData(Topology.TwoHopKey, MutationShape.IfVariableFalse)]
    [InlineData(Topology.TwoHopKey, MutationShape.InnerIfVariableTrue)]
    [InlineData(Topology.TwoHopKey, MutationShape.TwoRootFields)]
    [InlineData(Topology.TwoHopKey, MutationShape.ListField)]
    public async Task Defer_Should_RunMutationOncePerRootField_When_ShapeIsDeferred(
        Topology topology,
        MutationShape shape)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(topology);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, plain, rootFields) = GetRequests(shape);

        // act
        MutationSource.Reset();
        var deferredOutcome = await ExecuteAsync(client, deferred);
        MutationSource.Reset();
        var plainOutcome = await ExecuteAsync(client, plain);

        // assert
        Assert.Equal(rootFields, deferredOutcome.CreateCalls);
        Assert.Equal(rootFields, plainOutcome.CreateCalls);
        Assert.Null(deferredOutcome.Errors);
        Assert.True(
            JsonNode.DeepEquals(plainOutcome.Data, deferredOutcome.Data),
            $"Deferred: {deferredOutcome.Data}, plain: {plainOutcome.Data}");
    }

    [Theory]
    [MemberData(nameof(DeferConditionCases))]
    public async Task Defer_Should_MergeToNonDeferredData_When_DeferConditionsVaryWithKeyRequirement(
        Topology topology,
        OperationKind kind,
        ConditionShape shape,
        bool outer,
        bool inner)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(topology);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, plain) = GetConditionRequests(kind, shape, outer, inner);

        // act
        MutationSource.Reset();
        var deferredOutcome = await ExecuteAsync(client, deferred);
        MutationSource.Reset();
        var plainOutcome = await ExecuteAsync(client, plain);

        // assert
        // only the mutation root field is counted, the query root field is not
        Assert.Equal(kind is OperationKind.Mutation ? 1 : 0, deferredOutcome.CreateCalls);
        Assert.Null(deferredOutcome.Errors);
        Assert.True(
            JsonNode.DeepEquals(plainOutcome.Data, deferredOutcome.Data),
            $"Deferred: {deferredOutcome.Data}, plain: {plainOutcome.Data}");
    }

    [Fact]
    public async Task Defer_Should_MergeToNonDeferredData_When_NestedDeferNeedsTwoHopKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(Topology.TwoHopKey);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, _, _) = GetRequests(MutationShape.Nested);

        // act
        MutationSource.Reset();
        var outcome = await ExecuteAsync(client, deferred);

        // assert
        outcome.Data!.ToJsonString().MatchInlineSnapshot(
            """
            {"create":{"user":{"name":"user-1","total":42}}}
            """);
    }

    [Theory]
    [InlineData(Topology.DirectKey, MutationShape.AnchorOnMutationResult)]
    [InlineData(Topology.OneHopKey, MutationShape.NestedTotalFirst)]
    [InlineData(Topology.TwoHopKey, MutationShape.NestedTotalFirst)]
    [InlineData(Topology.DirectKey, MutationShape.ThreeLevels)]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.Nested)]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.Sibling)]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.NamedFragments)]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.IfVariableTrue)]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.TwoRootFields)]
    public async Task Defer_Should_NotRunMutation_When_ShapeIsRefusedAtPlanning(
        Topology topology,
        MutationShape shape)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(topology);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, _, _) = GetRequests(shape);

        // act
        MutationSource.Reset();
        var outcome = await ExecuteAsync(client, deferred);

        // assert
        Assert.Equal(0, outcome.CreateCalls);
        outcome.Errors!.ToJsonString().MatchInlineSnapshot(
            """
            [{"message":"Unexpected Execution Error"}]
            """);
    }

    public static TheoryData<Topology, OperationKind, ConditionShape, bool, bool> DeferConditionCases()
    {
        var cases = new TheoryData<Topology, OperationKind, ConditionShape, bool, bool>();

        foreach (var topology in new[] { Topology.DirectKey, Topology.OneHopKey, Topology.TwoHopKey })
        {
            foreach (var kind in new[] { OperationKind.Mutation, OperationKind.Query })
            {
                cases.Add(topology, kind, ConditionShape.Single, true, true);
                cases.Add(topology, kind, ConditionShape.Single, false, false);

                foreach (var outer in new[] { true, false })
                {
                    cases.Add(topology, kind, ConditionShape.OuterOnly, outer, false);
                    cases.Add(topology, kind, ConditionShape.InnerOnly, false, outer);

                    foreach (var inner in new[] { true, false })
                    {
                        cases.Add(topology, kind, ConditionShape.OuterAndInner, outer, inner);
                    }
                }
            }
        }

        return cases;
    }

    private static (OperationRequest Deferred, OperationRequest Plain) GetConditionRequests(
        OperationKind kind,
        ConditionShape shape,
        bool outer,
        bool inner)
    {
        var (keyword, root) = kind is OperationKind.Mutation
            ? ("mutation", "create")
            : ("query", "current");

        var plain = new OperationRequest($"{keyword} {{ {root} {{ user {{ name total }} }} }}");

        if (shape is ConditionShape.Single)
        {
            return (
                new OperationRequest(
                    $$"""
                    {{keyword}}($d: Boolean! = true) {
                        {{root}} {
                            user {
                                name
                                ... @defer(if: $d) { total }
                            }
                        }
                    }
                    """,
                    variables: new Dictionary<string, object?> { ["d"] = outer }),
                plain);
        }

        var outerArguments = shape is ConditionShape.InnerOnly ? "" : "if: $a, ";
        var innerArguments = shape is ConditionShape.OuterOnly ? "" : "if: $b, ";
        var variableDefinitions = shape switch
        {
            ConditionShape.OuterOnly => "$a: Boolean!",
            ConditionShape.InnerOnly => "$b: Boolean!",
            _ => "$a: Boolean!, $b: Boolean!"
        };
        var variables = shape switch
        {
            ConditionShape.OuterOnly => new Dictionary<string, object?> { ["a"] = outer },
            ConditionShape.InnerOnly => new Dictionary<string, object?> { ["b"] = inner },
            _ => new Dictionary<string, object?> { ["a"] = outer, ["b"] = inner }
        };

        return (
            new OperationRequest(
                $$"""
                {{keyword}}({{variableDefinitions}}) {
                    {{root}} {
                        user {
                            ... @defer({{outerArguments}}label: "outer") {
                                name
                                ... @defer({{innerArguments}}label: "inner") { total }
                            }
                        }
                    }
                }
                """,
                variables: variables),
            plain);
    }

    private static (OperationRequest Deferred, OperationRequest Plain, int RootFields) GetRequests(
        MutationShape shape)
    {
        const string plainUser = "mutation { create { user { name total } } }";

        return shape switch
        {
            MutationShape.Nested => (
                new OperationRequest(
                    """
                    mutation {
                        create {
                            user {
                                ... @defer(label: "outer") {
                                    name
                                    ... @defer(label: "inner") { total }
                                }
                            }
                        }
                    }
                    """),
                new OperationRequest(plainUser),
                1),
            MutationShape.NestedTotalFirst => (
                new OperationRequest(
                    """
                    mutation {
                        create {
                            user {
                                ... @defer(label: "outer") {
                                    total
                                    ... @defer(label: "inner") { name }
                                }
                            }
                        }
                    }
                    """),
                new OperationRequest(plainUser),
                1),
            MutationShape.Sibling => (
                new OperationRequest(
                    """
                    mutation {
                        create {
                            user {
                                ... @defer(label: "a") { name }
                                ... @defer(label: "b") { total }
                            }
                        }
                    }
                    """),
                new OperationRequest(plainUser),
                1),
            MutationShape.NamedFragments => (
                new OperationRequest(
                    """
                    mutation {
                        create {
                            user {
                                ...Outer @defer(label: "outer")
                            }
                        }
                    }

                    fragment Outer on User {
                        name
                        ...Inner @defer(label: "inner")
                    }

                    fragment Inner on User {
                        total
                    }
                    """),
                new OperationRequest(plainUser),
                1),
            MutationShape.IfVariableTrue or MutationShape.IfVariableFalse => (
                new OperationRequest(
                    """
                    mutation($defer: Boolean!) {
                        create {
                            user {
                                ... @defer(if: $defer, label: "outer") {
                                    name
                                    ... @defer(if: $defer, label: "inner") { total }
                                }
                            }
                        }
                    }
                    """,
                    variables: new Dictionary<string, object?>
                    {
                        ["defer"] = shape is MutationShape.IfVariableTrue
                    }),
                new OperationRequest(plainUser),
                1),
            MutationShape.InnerIfVariableTrue => (
                new OperationRequest(
                    """
                    mutation($defer: Boolean!) {
                        create {
                            user {
                                ... @defer(label: "outer") {
                                    name
                                    ... @defer(if: $defer, label: "inner") { total }
                                }
                            }
                        }
                    }
                    """,
                    variables: new Dictionary<string, object?> { ["defer"] = true }),
                new OperationRequest(plainUser),
                1),
            MutationShape.TwoRootFields => (
                new OperationRequest(
                    """
                    mutation {
                        a: create {
                            user {
                                ... @defer(label: "outer") {
                                    name
                                    ... @defer(label: "inner") { total }
                                }
                            }
                        }
                        b: create {
                            user {
                                ... @defer(label: "other") { name }
                            }
                        }
                    }
                    """),
                new OperationRequest(
                    "mutation { a: create { user { name total } } b: create { user { name } } }"),
                2),
            MutationShape.ListField => (
                new OperationRequest(
                    """
                    mutation {
                        createMany {
                            user {
                                ... @defer(label: "outer") {
                                    name
                                    ... @defer(label: "inner") { total }
                                }
                            }
                        }
                    }
                    """),
                new OperationRequest("mutation { createMany { user { name total } } }"),
                1),
            MutationShape.AnchorOnMutationResult => (
                new OperationRequest(
                    """
                    mutation {
                        create {
                            ... @defer(label: "outer") {
                                user {
                                    name
                                    ... @defer(label: "inner") { total }
                                }
                            }
                        }
                    }
                    """),
                new OperationRequest(plainUser),
                1),
            MutationShape.ThreeLevels => (
                new OperationRequest(
                    """
                    mutation {
                        create {
                            user {
                                ... @defer(label: "first") {
                                    id
                                    ... @defer(label: "second") {
                                        name
                                        ... @defer(label: "third") { total }
                                    }
                                }
                            }
                        }
                    }
                    """),
                new OperationRequest("mutation { create { user { id name total } } }"),
                1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private async Task<Gateway> CreateGatewayAsync(Topology topology)
    {
        var mutation = CreateSourceSchema(
            "A",
            b => b.AddQueryType<MutationSource.Query>().AddMutationType<MutationSource.Mutation>());

        var name = CreateSourceSchema("E", b => b.AddQueryType<NameSource.Query>());

        switch (topology)
        {
            case Topology.DirectKey:
                return await CreateCompositeSchemaAsync(
                [
                    ("A", mutation),
                    ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalByIdSource.Query>())),
                    ("E", name)
                ]);

            case Topology.OneHopKey:
                return await CreateCompositeSchemaAsync(
                [
                    ("A", mutation),
                    ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalBySkuSource.Query>())),
                    ("C", CreateSourceSchema("C", b => b.AddQueryType<SkuByIdSource.Query>())),
                    ("E", name)
                ]);

            case Topology.TwoHopKey:
                return await CreateCompositeSchemaAsync(
                [
                    ("A", mutation),
                    ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalBySkuSource.Query>())),
                    ("C", CreateSourceSchema("C", b => b.AddQueryType<CodeByIdSource.Query>())),
                    ("D", CreateSourceSchema("D", b => b.AddQueryType<SkuByCodeSource.Query>())),
                    ("E", name)
                ]);

            default:
                return await CreateCompositeSchemaAsync(
                [
                    (
                        "A",
                        CreateSourceSchema(
                            "A",
                            b => b.AddQueryType<NamedMutationSource.Query>()
                                .AddMutationType<NamedMutationSource.Mutation>())),
                    ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalByIdSource.Query>()))
                ]);
        }
    }

    private static async Task<Outcome> ExecuteAsync(GraphQLHttpClient client, OperationRequest request)
    {
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        var body = await result.HttpResponseMessage.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        JsonNode? data = null;
        JsonNode? errors = null;
        var pendingPaths = new Dictionary<string, JsonArray>();
        var openIds = new HashSet<string>();
        var violations = new List<string>();
        var lastHasNext = false;

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = JsonNode.Parse(line)!.AsObject();
            data ??= payload["data"]?.DeepClone();
            errors ??= payload["errors"]?.DeepClone();
            lastHasNext = payload["hasNext"]?.GetValue<bool>() ?? false;

            if (payload["pending"] is JsonArray pending)
            {
                foreach (var entry in pending)
                {
                    var id = entry!["id"]!.GetValue<string>();

                    if (!pendingPaths.TryAdd(id, entry["path"]!.AsArray()))
                    {
                        violations.Add($"Pending id '{id}' was announced more than once.");
                    }

                    openIds.Add(id);
                }
            }

            if (payload["incremental"] is JsonArray incremental)
            {
                foreach (var item in incremental)
                {
                    var id = item!["id"]!.GetValue<string>();

                    if (!openIds.Contains(id))
                    {
                        violations.Add($"Incremental entry refers to id '{id}' which is not announced and open.");
                        continue;
                    }

                    var target = data;
                    var path = pendingPaths[id].Concat(item["subPath"] as JsonArray ?? []);

                    foreach (var segment in path)
                    {
                        target = segment!.GetValueKind() is JsonValueKind.Number
                            ? target?[segment.GetValue<int>()]
                            : target?[segment.GetValue<string>()];
                    }

                    if (target is JsonObject targetObject && item["data"] is JsonObject itemData)
                    {
                        MergeInto(targetObject, itemData);
                    }
                }
            }

            if (payload["completed"] is JsonArray completed)
            {
                foreach (var entry in completed)
                {
                    errors ??= entry!["errors"]?.DeepClone();

                    var id = entry!["id"]!.GetValue<string>();

                    if (!openIds.Remove(id))
                    {
                        violations.Add($"Completed id '{id}' is not announced or was already completed.");
                    }
                }
            }
        }

        foreach (var id in openIds)
        {
            violations.Add($"Pending id '{id}' was never completed.");
        }

        // the response stream is fully consumed at this point, so the call count is final
        Assert.Empty(violations);
        Assert.False(lastHasNext, "The last payload must have hasNext false.");

        return new Outcome(data, errors, MutationSource.CreateCalls);
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source)
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

    public enum Topology
    {
        DirectKey,
        OneHopKey,
        TwoHopKey,
        NameOnMutationSubgraph
    }

    public enum OperationKind
    {
        Mutation,
        Query
    }

    public enum ConditionShape
    {
        Single,
        OuterAndInner,
        OuterOnly,
        InnerOnly
    }

    public enum MutationShape
    {
        Nested,
        NestedTotalFirst,
        Sibling,
        NamedFragments,
        IfVariableTrue,
        IfVariableFalse,
        InnerIfVariableTrue,
        TwoRootFields,
        ListField,
        AnchorOnMutationResult,
        ThreeLevels
    }

    private sealed record Outcome(JsonNode? Data, JsonNode? Errors, int CreateCalls);

    private static class MutationSource
    {
        private static int s_createCalls;

        public static int CreateCalls => Volatile.Read(ref s_createCalls);

        public static void Reset() => Volatile.Write(ref s_createCalls, 0);

        public static void Count() => Interlocked.Increment(ref s_createCalls);

        public class Query
        {
            public string GetPing() => "pong";

            public CreateResult GetCurrent() => new(new User(1));

            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public class Mutation
        {
            public CreateResult Create()
            {
                Count();
                return new CreateResult(new User(1));
            }

            public CreateResult[] CreateMany()
            {
                Count();
                return
                [
                    new CreateResult(new User(1)),
                    new CreateResult(new User(2)),
                    new CreateResult(new User(3))
                ];
            }
        }

        public record CreateResult(User User);

        public record User([property: ID] int Id);
    }

    private static class NamedMutationSource
    {
        public class Query
        {
            public string GetPing() => "pong";

            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id, $"user-{id}");
        }

        public class Mutation
        {
            public CreateResult Create()
            {
                MutationSource.Count();
                return new CreateResult(new User(1, "user-1"));
            }

            public CreateResult[] CreateMany()
            {
                MutationSource.Count();
                return
                [
                    new CreateResult(new User(1, "user-1")),
                    new CreateResult(new User(2, "user-2")),
                    new CreateResult(new User(3, "user-3"))
                ];
            }
        }

        public record CreateResult(User User);

        public record User([property: ID] int Id, string Name);
    }

    private static class NameSource
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public record User([property: ID] int Id)
        {
            public string Name => $"user-{Id}";
        }
    }

    private static class TotalByIdSource
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public record User([property: ID] int Id)
        {
            public int Total => 41 + Id;
        }
    }

    private static class TotalBySkuSource
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserBySku(string sku) => new(sku);
        }

        public record User([property: Shareable] string Sku)
        {
            public int Total => 41 + int.Parse(Sku[(Sku.LastIndexOf('-') + 1)..]);
        }
    }

    private static class SkuByIdSource
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public record User([property: ID] int Id)
        {
            [Shareable]
            public string Sku => $"sku-{Id}";
        }
    }

    private static class CodeByIdSource
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public record User([property: ID] int Id)
        {
            [Shareable]
            public string Code => $"code-{Id}";
        }
    }

    private static class SkuByCodeSource
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserByCode(string code) => new(code);
        }

        public record User([property: Shareable] string Code)
        {
            [Shareable]
            public string Sku => $"sku-{Code}";
        }
    }
}
