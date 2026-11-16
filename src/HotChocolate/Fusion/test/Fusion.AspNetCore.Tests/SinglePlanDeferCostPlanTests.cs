using System.Diagnostics;
using HotChocolate.Execution;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Transport.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

/// <summary>
/// Verifies the cost plan of operations that use <c>@defer</c> with the single plan option on.
/// </summary>
[Collection(SinglePlanDeferCostCollection.Name)]
public class SinglePlanDeferCostPlanTests : FusionTestBase
{
    private const int MaxChainDepth = 24;
    private const int ChainDepth = 20;
    private const int WarmUpDepth = 4;
    private static readonly TimeSpan s_costAnalysisBound = TimeSpan.FromMilliseconds(100);
    private const string CostValuesSchemaText =
        """
        directive @cost(weight: String!)
          on ARGUMENT_DEFINITION | ENUM | FIELD_DEFINITION | INPUT_FIELD_DEFINITION | OBJECT | SCALAR
        directive @listSize(
          assumedSize: Int,
          slicingArguments: [String!],
          sizedFields: [String!],
          requireOneSlicingArgument: Boolean = true)
          on FIELD_DEFINITION

        type Query { product(id: ID!): Product }

        type Product {
          id: ID!
          name: String!
          n: N1!
          j: J!
          tags: [Tag!]! @listSize(assumedSize: 4)
          items(first: Int): [Item!]! @listSize(slicingArguments: ["first"], assumedSize: 10)
          groups: [Group!]! @listSize(assumedSize: 3)
        }

        interface J { n: J! v: Int! }
        type JA implements J { n: J! v: Int! }
        type JB implements J { n: J! v: Int! }
        type Tag { label: String! @cost(weight: "3") note: String }
        type Item @cost(weight: "2") { value: Int! @cost(weight: "5") weight: Float! }
        type Group { id: ID! items: [Item!]! @listSize(assumedSize: 5) }
        """;

    private static readonly Uri s_endpoint = new("http://localhost:5000/graphql");

    [Fact]
    public async Task CostAnalysis_Should_FinishInMilliseconds_When_ADeferredChainStandsBelowAnEqualPlainChain()
    {
        // arrange
        var timer = new CostAnalysisTimer();
        using var server = CreateSourceSchema("A", CreateChainSchema());
        using var gateway = await CreateGatewayAsync(server, enableSinglePlanDefer: true, timer);
        await ValidateCostAsync(gateway, CreateChainQuery(WarmUpDepth));

        // act
        var cost = await ValidateCostAsync(gateway, CreateChainQuery(ChainDepth));

        // assert
        cost.MatchInlineSnapshot("fieldCost: 21, typeCost: 22");
        Assert.True(
            timer.Last < s_costAnalysisBound,
            $"The cost analysis took {timer.Last.TotalMilliseconds:F1} ms, the bound is "
            + $"{s_costAnalysisBound.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task CostAnalysis_Should_MatchTheOptionOff_When_TheOperationHasDefer()
    {
        // arrange
        using var serverWithOption = CreateSourceSchema("A", CostValuesSchemaText + CreateChainTypes());
        using var serverWithoutOption = CreateSourceSchema("A", CostValuesSchemaText + CreateChainTypes());
        using var withOption = await CreateGatewayAsync(serverWithOption, enableSinglePlanDefer: true);
        using var withoutOption = await CreateGatewayAsync(serverWithoutOption, enableSinglePlanDefer: false);
        var on = new List<string>();
        var off = new List<string>();

        // act
        foreach (var (name, query, variables) in CreateCostValueOperations())
        {
            on.Add($"{name}: {await ValidateCostAsync(withOption, query, variables)}");
            off.Add($"{name}: {await ValidateCostAsync(withoutOption, query, variables)}");
        }

        // assert
        Assert.Equal(off, on);
        on.MatchInlineSnapshots(
        [
            "both 3: fieldCost: 4, typeCost: 5",
            "if false 3: fieldCost: 4, typeCost: 5",
            "if true 3: fieldCost: 4, typeCost: 5",
            "three occurrences 3: fieldCost: 4, typeCost: 5",
            "both 6: fieldCost: 7, typeCost: 8",
            "if false 6: fieldCost: 7, typeCost: 8",
            "if true 6: fieldCost: 7, typeCost: 8",
            "three occurrences 6: fieldCost: 7, typeCost: 8",
            "interface 3: fieldCost: 5, typeCost: 6",
            "list: fieldCost: 14, typeCost: 6",
            "slicing: fieldCost: 17, typeCost: 8",
            "nested lists: fieldCost: 80, typeCost: 35"
        ]);
    }

    private static IEnumerable<(string Name, string Query, Dictionary<string, object?>? Variables)>
        CreateCostValueOperations()
    {
        foreach (var depth in new[] { 3, 6 })
        {
            var plain = CreateChain(depth, "v");
            var deferred = CreateChain(depth, "w");
            var product = $"product(id: \"1\") {{ name {plain}";

            yield return ($"both {depth}", $"{{ {product} ... @defer(label: \"d\") {{ {deferred} }} }} }}", null);
            yield return (
                $"if false {depth}",
                $"query($v: Boolean!) {{ {product} ... @defer(if: $v, label: \"d\") {{ {deferred} }} }} }}",
                new Dictionary<string, object?> { ["v"] = false });
            yield return (
                $"if true {depth}",
                $"query($v: Boolean!) {{ {product} ... @defer(if: $v, label: \"d\") {{ {deferred} }} }} }}",
                new Dictionary<string, object?> { ["v"] = true });
            yield return (
                $"three occurrences {depth}",
                $"{{ {product} ... @defer(label: \"d\") {{ {deferred} }} "
                + $"... @defer(label: \"e\") {{ {deferred} }} }} }}",
                null);
        }

        yield return (
            "interface 3",
            "{ product(id: \"1\") { name j { n { n { n { v } } } } "
            + "... @defer(label: \"d\") { j { n { n { n { v } } } } } } }",
            null);
        yield return (
            "list",
            "{ product(id: \"1\") { name tags { label } ... @defer(label: \"d\") { tags { label note } } } }",
            null);
        yield return (
            "slicing",
            "query($n: Int!) { product(id: \"1\") { items(first: $n) { value } "
            + "... @defer(label: \"d\") { items(first: $n) { value weight } } } }",
            new Dictionary<string, object?> { ["n"] = 3 });
        yield return (
            "nested lists",
            "query($v: Boolean!) { product(id: \"1\") { groups { id items { value } } "
            + "... @defer(if: $v, label: \"d\") { groups { items { weight } } } } }",
            new Dictionary<string, object?> { ["v"] = true });
    }

    // The deferred chain below an equal plain chain: every level selects the same field in both parts.
    private static string CreateChainQuery(int depth)
    {
        var plain = CreateChain(depth, "v");
        var deferred = CreateChain(depth, "w");
        return $"{{ product(id: \"1\") {{ name {plain} ... @defer(label: \"d\") {{ {deferred} }} }} }}";
    }

    private static string CreateChain(int depth, string leaf)
        => string.Concat(Enumerable.Repeat("n { ", depth)) + leaf + string.Concat(Enumerable.Repeat(" }", depth));

    private static string CreateChainTypes()
        => string.Join(
            "\n",
            Enumerable.Range(1, MaxChainDepth).Select(i => i == MaxChainDepth
                ? $"type N{i} {{ v: Int! w: Float! }}"
                : $"type N{i} {{ n: N{i + 1}! v: Int! w: Float! }}"));

    private static async Task<string> ValidateCostAsync(
        Gateway gateway,
        string query,
        Dictionary<string, object?>? variables = null)
    {
        var request = new OperationRequest(query, variables: variables);

        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.SendAsync(
            new GraphQLHttpRequest(request, s_endpoint)
            {
                OnMessageCreated = (_, message, _) => message.Headers.Add("GraphQL-Cost", "validate")
            },
            TestContext.Current.CancellationToken);
        using var result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);

        var cost = result.Extensions.GetProperty("operationCost");
        return $"fieldCost: {cost.GetProperty("fieldCost")}, typeCost: {cost.GetProperty("typeCost")}";
    }

    private static string CreateChainSchema()
        => "type Query { product(id: ID!): Product }\n"
            + "type Product { id: ID! name: String! n: N1! }\n"
            + CreateChainTypes();

    private async Task<Gateway> CreateGatewayAsync(
        TestServer server,
        bool enableSinglePlanDefer,
        CostAnalysisTimer? timer = null)
    {
        return await CreateCompositeSchemaAsync(
            [("A", server)],
            configureGatewayBuilder: builder =>
            {
                builder.ModifyPlannerOptions(options => options.EnableSinglePlanDefer = enableSinglePlanDefer);

                if (timer is not null)
                {
                    builder.AddDiagnosticEventListener(_ => timer);
                }
            });
    }

    private sealed class CostAnalysisTimer : FusionExecutionDiagnosticEventListener
    {
        public TimeSpan Last { get; private set; }

        public override IDisposable AnalyzeOperationCost(RequestContext context) => new Scope(this);

        private sealed class Scope(CostAnalysisTimer timer) : IDisposable
        {
            private readonly long _start = Stopwatch.GetTimestamp();

            public void Dispose() => timer.Last = Stopwatch.GetElapsedTime(_start);
        }
    }
}
