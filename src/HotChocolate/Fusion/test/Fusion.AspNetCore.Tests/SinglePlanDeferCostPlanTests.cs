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
    private static readonly Uri s_endpoint = new("http://localhost:5000/graphql");

    [Fact]
    public async Task CostAnalysis_Should_FinishInMilliseconds_When_ADeferredChainStandsBelowAnEqualPlainChain()
    {
        // arrange
        var timer = new CostAnalysisTimer();
        using var server = CreateSourceSchema("A", CreateSchema());
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

    private static string CreateSchema()
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
