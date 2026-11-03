using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

public class DeferKeyHopTests : FusionTestBase
{
    public static TheoryData<Topology, OperationForm, DeferShape, bool> Cases
    {
        get
        {
            var data = new TheoryData<Topology, OperationForm, DeferShape, bool>();

            foreach (var topology in Enum.GetValues<Topology>())
            {
                foreach (var form in Enum.GetValues<OperationForm>())
                {
                    foreach (var shape in Enum.GetValues<DeferShape>())
                    {
                        data.Add(topology, form, shape, true);

                        switch (shape)
                        {
                            case DeferShape.IdAndName or DeferShape.NestedChain:
                                break;

                            case DeferShape.ConditionalNestedChain:
                                data.Add(
                                    new TheoryDataRow<Topology, OperationForm, DeferShape, bool>(
                                        topology, form, shape, false)
                                    {
                                        Skip = "A nested @defer under a disabled @defer loses its fields."
                                    });
                                break;

                            case DeferShape.ConditionalAndUnconditionalSameField:
                                data.Add(
                                    new TheoryDataRow<Topology, OperationForm, DeferShape, bool>(
                                        topology, form, shape, false)
                                    {
                                        Skip = "The unconditional group delivers total again after the initial payload."
                                    });
                                break;

                            default:
                                data.Add(topology, form, shape, false);
                                break;
                        }
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Defer_Should_MergeToNonDeferredData_When_GroupNeedsKeyHop(
        Topology topology,
        OperationForm form,
        DeferShape shape,
        bool deferEnabled)
    {
        // arrange
        using var gateway = await CreateGatewayAsync(topology);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, plain) = GetRequests(form, shape, deferEnabled);

        // act
        var deferredOutcome = await ExecuteAsync(client, deferred);
        var plainOutcome = await ExecuteAsync(client, plain);

        // assert
        Assert.Null(deferredOutcome.Errors);
        Assert.Empty(deferredOutcome.Violations);
        Assert.True(
            JsonNode.DeepEquals(plainOutcome.Data, deferredOutcome.Data),
            $"Deferred: {deferredOutcome.Data?.ToJsonString()}, plain: {plainOutcome.Data?.ToJsonString()}");
    }

    private static (OperationRequest Deferred, OperationRequest Plain) GetRequests(
        OperationForm form,
        DeferShape shape,
        bool deferEnabled)
    {
        var (operationType, rootField) = form switch
        {
            OperationForm.Query => ("query", "current"),
            OperationForm.Mutation => ("mutation", "create"),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

        var (selection, plainSelection) = shape switch
        {
            DeferShape.Conditional =>
                ("... @defer(if: $a) { name total }", "name total"),
            DeferShape.ConditionalAndUnconditionalSiblings =>
                ("... @defer(if: $a) { total } ... @defer { name }", "name total"),
            DeferShape.ConditionalAndUnconditionalSameField =>
                ("... @defer(if: $a) { total } ... @defer { total }", "total"),
            DeferShape.IdAndName =>
                ("... @defer { id name }", "id name"),
            DeferShape.NestedChain =>
                ("... @defer { id ... @defer { name ... @defer { total } } }", "id name total"),
            DeferShape.ConditionalNestedChain =>
                ("... @defer(if: $a) { id ... @defer { name ... @defer { total } } }", "id name total"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var plain = new OperationRequest(
            $"{operationType} {{ {rootField} {{ user {{ {plainSelection} }} }} }}");

        if (shape is DeferShape.IdAndName or DeferShape.NestedChain)
        {
            return (
                new OperationRequest($"{operationType} {{ {rootField} {{ user {{ {selection} }} }} }}"),
                plain);
        }

        return (
            new OperationRequest(
                $"{operationType}($a: Boolean!) {{ {rootField} {{ user {{ {selection} }} }} }}",
                variables: new Dictionary<string, object?> { ["a"] = deferEnabled }),
            plain);
    }

    private async Task<Gateway> CreateGatewayAsync(Topology topology)
    {
        var root = CreateSourceSchema(
            "A",
            b => b.AddQueryType<RootSource.Query>().AddMutationType<RootSource.Mutation>());

        var name = CreateSourceSchema("E", b => b.AddQueryType<NameSource.Query>());

        return topology switch
        {
            Topology.DirectKey => await CreateCompositeSchemaAsync(
            [
                ("A", root),
                ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalByIdSource.Query>())),
                ("E", name)
            ]),
            Topology.OneHopKey => await CreateCompositeSchemaAsync(
            [
                ("A", root),
                ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalBySkuSource.Query>())),
                ("C", CreateSourceSchema("C", b => b.AddQueryType<SkuByIdSource.Query>())),
                ("E", name)
            ]),
            Topology.TwoHopKey => await CreateCompositeSchemaAsync(
            [
                ("A", root),
                ("B", CreateSourceSchema("B", b => b.AddQueryType<TotalBySkuSource.Query>())),
                ("C", CreateSourceSchema("C", b => b.AddQueryType<CodeByIdSource.Query>())),
                ("D", CreateSourceSchema("D", b => b.AddQueryType<SkuByCodeSource.Query>())),
                ("E", name)
            ]),
            _ => throw new ArgumentOutOfRangeException(nameof(topology))
        };
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
                        violations.Add($"Incremental entry refers to id '{id}' which is not open.");
                        continue;
                    }

                    errors ??= item["errors"]?.DeepClone();

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
                        MergeInto(targetObject, itemData, violations);
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
                        violations.Add($"Completed id '{id}' is not open.");
                    }
                }
            }
        }

        foreach (var id in openIds)
        {
            violations.Add($"Pending id '{id}' was never completed.");
        }

        if (lastHasNext)
        {
            violations.Add("The last payload has hasNext true.");
        }

        return new Outcome(data, errors, violations);
    }

    private static void MergeInto(JsonObject target, JsonObject source, List<string> violations)
    {
        foreach (var (name, value) in source)
        {
            if (value is JsonObject sourceObject && target[name] is JsonObject targetObject)
            {
                MergeInto(targetObject, sourceObject, violations);
            }
            else
            {
                if (target.ContainsKey(name))
                {
                    violations.Add($"The field '{name}' was delivered more than once.");
                }

                target[name] = value?.DeepClone();
            }
        }
    }

    public enum Topology
    {
        DirectKey,
        OneHopKey,
        TwoHopKey
    }

    public enum OperationForm
    {
        Query,
        Mutation
    }

    public enum DeferShape
    {
        Conditional,
        ConditionalAndUnconditionalSiblings,
        ConditionalAndUnconditionalSameField,
        IdAndName,
        NestedChain,
        ConditionalNestedChain
    }

    private sealed record Outcome(JsonNode? Data, JsonNode? Errors, List<string> Violations);

    private static class RootSource
    {
        public class Query
        {
            public CreateResult GetCurrent() => new(new User(1));

            [Lookup]
            [Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public class Mutation
        {
            public CreateResult Create() => new(new User(1));
        }

        public record CreateResult(User User);

        public record User([property: ID] int Id);
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
