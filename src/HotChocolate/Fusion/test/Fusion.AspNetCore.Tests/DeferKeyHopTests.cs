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
    public static TheoryData<Topology, OperationForm, DeferShape, bool?, bool?> Cases
    {
        get
        {
            var data = new TheoryData<Topology, OperationForm, DeferShape, bool?, bool?>();

            foreach (var topology in Enum.GetValues<Topology>())
            {
                foreach (var form in Enum.GetValues<OperationForm>())
                {
                    foreach (var shape in Enum.GetValues<DeferShape>())
                    {
                        var (selection, _) = GetSelections(shape);
                        bool?[] aValues = selection.Contains("$a") ? [true, false] : [null];
                        bool?[] bValues = selection.Contains("$b") ? [true, false] : [null];

                        foreach (var a in aValues)
                        {
                            foreach (var b in bValues)
                            {
                                var row = new TheoryDataRow<Topology, OperationForm, DeferShape, bool?, bool?>(
                                    topology, form, shape, a, b)
                                {
                                    Skip = GetSkipReason(topology, form, shape, a)
                                };
                                data.Add(row);
                            }
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
        bool? a,
        bool? b)
    {
        // arrange
        var mutationCounter = new MutationCounter();
        using var gateway = await CreateGatewayAsync(topology, mutationCounter);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var (deferred, plain) = GetRequests(form, shape, a, b);

        // act
        var deferredOutcome = await ExecuteAsync(client, deferred);
        var deferredMutationCount = mutationCounter.Count;
        var plainOutcome = await ExecuteAsync(client, plain);

        // assert
        Assert.Null(deferredOutcome.Errors);
        Assert.Empty(deferredOutcome.Violations);
        Assert.True(
            JsonNode.DeepEquals(plainOutcome.Data, deferredOutcome.Data),
            $"Deferred: {deferredOutcome.Data?.ToJsonString()}, plain: {plainOutcome.Data?.ToJsonString()}");
        Assert.Equal(form is OperationForm.Mutation ? 1 : 0, deferredMutationCount);
    }

    private static string? GetSkipReason(Topology topology, OperationForm form, DeferShape shape, bool? a)
        => (shape, a) switch
        {
            (DeferShape.ConditionalNestedChain, false) =>
                "A nested @defer under a disabled @defer loses its fields.",
            (DeferShape.ConditionalAndUnconditionalSameField, false) =>
                "The unconditional group delivers total again after the initial payload.",
            (DeferShape.NestedIncludedKeyAndHopField, false) when topology is not Topology.DirectKey =>
                "The enclosing group loses its fields when the nested group behind a key hop is excluded.",
            (DeferShape.AliasedKeyAndHopField, _) when form is OperationForm.Mutation =>
                "A deferred group with an aliased key field cannot be planned under a mutation.",
            _ => null
        };

    private static (string Selection, string PlainSelection) GetSelections(DeferShape shape)
        => shape switch
        {
            DeferShape.Conditional =>
                ("user { ... @defer(if: $a) { name total } }", "user { name total }"),
            DeferShape.ConditionalAndUnconditionalSiblings =>
                ("user { ... @defer(if: $a) { total } ... @defer { name } }", "user { name total }"),
            DeferShape.ConditionalAndUnconditionalSameField =>
                ("user { ... @defer(if: $a) { total } ... @defer { total } }", "user { total }"),
            DeferShape.IdAndName =>
                ("user { ... @defer { id name } }", "user { id name }"),
            DeferShape.NestedChain =>
                ("user { ... @defer { id ... @defer { name ... @defer { total } } } }", "user { id name total }"),
            DeferShape.ConditionalNestedChain =>
                ("user { ... @defer(if: $a) { id ... @defer { name ... @defer { total } } } }",
                    "user { id name total }"),
            DeferShape.IncludedGroup =>
                ("user { ... @defer @include(if: $a) { name total } }", "user { ... @include(if: $a) { name total } }"),
            DeferShape.SkippedGroup =>
                ("user { ... @defer @skip(if: $a) { name total } }", "user { ... @skip(if: $a) { name total } }"),
            DeferShape.IncludedGroupWithKey =>
                ("user { ... @defer @include(if: $a) { id name } }", "user { ... @include(if: $a) { id name } }"),
            DeferShape.ConditionalSkippedGroup =>
                ("user { ... @defer(if: $b) @skip(if: $a) { name total } }",
                    "user { ... @skip(if: $a) { name total } }"),
            DeferShape.InnerSkipUnderConditional =>
                ("user { ... @defer(if: $b) { ... @skip(if: $a) { name total } } }",
                    "user { ... @skip(if: $a) { name total } }"),
            DeferShape.InnerIncludeWithKey =>
                ("user { ... @defer { ... @include(if: $a) { id total } } }",
                    "user { ... @include(if: $a) { id total } }"),
            DeferShape.IncludedKeyField =>
                ("user { ... @defer { id @include(if: $a) name } }", "user { id @include(if: $a) name }"),
            DeferShape.KeyAndHopField =>
                ("user { ... @defer { id total } }", "user { id total }"),
            DeferShape.AliasedKeyAndHopField =>
                ("user { ... @defer { k: id total } }", "user { k: id total }"),
            DeferShape.ConditionalKeyAndHopField =>
                ("user { ... @defer(if: $a) { id total } }", "user { id total }"),
            DeferShape.IncludedGroupKeyAndHopField =>
                ("user { ... @defer @include(if: $a) { id total } }", "user { ... @include(if: $a) { id total } }"),
            DeferShape.SkippedGroupKeyAndHopField =>
                ("user { ... @defer @skip(if: $a) { id total } }", "user { ... @skip(if: $a) { id total } }"),
            DeferShape.SiblingsKeyAndHopField =>
                ("user { ... @defer(if: $a) { id total } ... @defer(if: $b) { name } }", "user { id total name }"),
            DeferShape.NestedIncludedKeyAndHopField =>
                ("user { ... @defer { name ... @defer @include(if: $a) { id total } } }",
                    "user { name ... @include(if: $a) { id total } }"),
            DeferShape.ListKeyAndHopField =>
                ("users { ... @defer { id total } }", "users { id total }"),
            DeferShape.ConditionalListKeyAndHopField =>
                ("users { ... @defer(if: $a) { id total } }", "users { id total }"),
            DeferShape.FriendChainKeyAndHopField =>
                ("users { friend { friend { ... @defer(if: $a) { id total } } } }",
                    "users { friend { friend { id total } } }"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

    private static (OperationRequest Deferred, OperationRequest Plain) GetRequests(
        OperationForm form,
        DeferShape shape,
        bool? a,
        bool? b)
    {
        var (operationType, rootField) = form switch
        {
            OperationForm.Query => ("query", "current"),
            OperationForm.Mutation => ("mutation", "create"),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

        var (selection, plainSelection) = GetSelections(shape);

        return (
            CreateRequest(operationType, rootField, selection, a, b),
            CreateRequest(operationType, rootField, plainSelection, a, b));
    }

    private static OperationRequest CreateRequest(
        string operationType,
        string rootField,
        string selection,
        bool? a,
        bool? b)
    {
        var declarations = new List<string>();
        var variables = new Dictionary<string, object?>();

        foreach (var (name, value) in new[] { ("a", a), ("b", b) })
        {
            if (selection.Contains("$" + name))
            {
                declarations.Add($"${name}: Boolean!");
                variables[name] = value;
            }
        }

        var header = declarations.Count == 0 ? operationType : $"{operationType}({string.Join(", ", declarations)})";
        var text = $"{header} {{ {rootField} {{ {selection} }} }}";

        return variables.Count == 0 ? new OperationRequest(text) : new OperationRequest(text, variables: variables);
    }

    private async Task<Gateway> CreateGatewayAsync(Topology topology, MutationCounter mutationCounter)
    {
        var root = CreateSourceSchema(
            "A",
            b => b.AddQueryType<RootSource.Query>().AddMutationType<RootSource.Mutation>(),
            configureServices: services => services.AddSingleton(mutationCounter));

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
        ConditionalNestedChain,
        IncludedGroup,
        SkippedGroup,
        IncludedGroupWithKey,
        ConditionalSkippedGroup,
        InnerSkipUnderConditional,
        InnerIncludeWithKey,
        IncludedKeyField,
        KeyAndHopField,
        AliasedKeyAndHopField,
        ConditionalKeyAndHopField,
        IncludedGroupKeyAndHopField,
        SkippedGroupKeyAndHopField,
        SiblingsKeyAndHopField,
        NestedIncludedKeyAndHopField,
        ListKeyAndHopField,
        ConditionalListKeyAndHopField,
        FriendChainKeyAndHopField
    }

    private sealed record Outcome(JsonNode? Data, JsonNode? Errors, List<string> Violations);

    private sealed class MutationCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => Interlocked.Increment(ref _count);
    }

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
            public CreateResult Create([Service] MutationCounter mutationCounter)
            {
                mutationCounter.Increment();
                return new CreateResult(new User(1));
            }
        }

        public record CreateResult(User User)
        {
            public User[] Users => [new User(1), new User(2)];
        }

        public record User([property: ID] int Id)
        {
            public User Friend => new(Id + 1);
        }
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
