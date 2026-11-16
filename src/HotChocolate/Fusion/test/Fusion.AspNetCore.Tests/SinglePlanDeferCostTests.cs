using HotChocolate.Execution;
using HotChocolate.Fusion.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SinglePlanDeferCostCollection
{
    public const string Name = "SinglePlanDeferCost";
}

[Collection(SinglePlanDeferCostCollection.Name)]
public class SinglePlanDeferCostTests : FusionTestBase
{
    private const int WarmUpRequests = 60;
    private const int Samples = 10;
    private static readonly TimeSpan s_settle = TimeSpan.FromMilliseconds(20);

    private const string ProductSource =
        """
        type Query { product(id: ID!): Product @lookup products: [Product!]! }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string PriceSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! price: Float! }
        """;

    [Theory]
    [InlineData("query($skip: Boolean = false) { product(id: \"1\") { name price @skip(if: $skip) } }", 12)]
    [InlineData("{ products { id name } }", 27)]
    [InlineData("{ product(id: \"1\") { name price } }", 12)]
    public async Task Execute_Should_MatchTheOptionOff_When_TheOperationHasNoDefer(string query, int expectedRows)
    {
        // arrange
        using var withOption = await CreateGatewayAsync(enableSinglePlanDefer: true);
        using var withoutOption = await CreateGatewayAsync(enableSinglePlanDefer: false);
        var onExecutor = await GetExecutorAsync(withOption);
        var offExecutor = await GetExecutorAsync(withoutOption);
        await WarmUpAsync(onExecutor, offExecutor, query);

        // act
        var (on, off) = await MeasureAsync(onExecutor, offExecutor, query);

        // assert
        Assert.Equal(expectedRows.ToString(), off.Rows);
        Assert.Equal(off.Rows, on.Rows);
        Assert.Equal(off.Bytes, on.Bytes);
        Assert.False(on.HasDelivery || off.HasDelivery);
    }

    private static async Task WarmUpAsync(IRequestExecutor onExecutor, IRequestExecutor offExecutor, string query)
    {
        // A second round after a pause lets the tiered compilation settle on the optimized code.
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < WarmUpRequests; i++)
            {
                await RunAsync(onExecutor, query, settle: false);
                await RunAsync(offExecutor, query, settle: false);
            }

            await Task.Delay(300, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<(Measurement On, Measurement Off)> MeasureAsync(
        IRequestExecutor onExecutor,
        IRequestExecutor offExecutor,
        string query)
    {
        var on = Measurement.Empty;
        var off = Measurement.Empty;

        for (var i = 0; i < Samples; i++)
        {
            on = on.Merge(await RunAsync(onExecutor, query, settle: true));
            off = off.Merge(await RunAsync(offExecutor, query, settle: true));
        }

        return (on, off);
    }

    // The window covers the request, the disposal of its result and a pause for the work that runs after the
    // executor returned. Pools refill now and then, which only adds bytes, so a measurement keeps the smallest
    // window of its samples.
    private static async Task<Measurement> RunAsync(IRequestExecutor executor, string query, bool settle)
    {
        if (settle)
        {
            await Task.Delay(s_settle, TestContext.Current.CancellationToken);
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = await executor.ExecuteAsync(
            OperationRequest.FromSourceText(query),
            TestContext.Current.CancellationToken);
        var document = (CompositeResultDocument)((OperationResult)result).Data!.Value.Value;
        var rows = document._metaDb.NextCursor.Value;
        var hasDelivery = document.Delivery is not null;
        await result.DisposeAsync();

        if (settle)
        {
            await Task.Delay(s_settle, TestContext.Current.CancellationToken);
        }

        return new Measurement(GC.GetTotalAllocatedBytes(precise: true) - before, [rows], hasDelivery);
    }

    private static async Task<IRequestExecutor> GetExecutorAsync(Gateway gateway)
        => await gateway.Services.GetRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private async Task<Gateway> CreateGatewayAsync(bool enableSinglePlanDefer)
        => await CreateCompositeSchemaAsync(
            [
                CreateSlowSourceSchema("A", ProductSource),
                CreateSlowSourceSchema("B", PriceSource)
            ],
            configureGatewayBuilder: builder => builder
                .ModifyPlannerOptions(options => options.EnableSinglePlanDefer = enableSinglePlanDefer));

    // A response that takes a moment makes the executor wait for every step, so that each request suspends in the
    // same places.
    private (string Name, TestServer Server) CreateSlowSourceSchema(string name, string schemaText)
    {
        HttpMessageInvoker? invoker = null;
        var server = CreateSourceSchema(
            name,
            schemaText,
            mockHttpResponse: async request =>
            {
                await request.Content!.LoadIntoBufferAsync();
                await Task.Delay(2);
                return await invoker!.SendAsync(request, CancellationToken.None);
            });
        invoker = new HttpMessageInvoker(server.CreateHandler());
        return (name, server);
    }

    private sealed record Measurement(long Bytes, SortedSet<int> RowSet, bool HasDelivery)
    {
        public static Measurement Empty { get; } = new(long.MaxValue, [], HasDelivery: false);

        public string Rows => string.Join(",", RowSet);

        public Measurement Merge(Measurement other)
            => new(
                Math.Min(Bytes, other.Bytes),
                [.. RowSet, .. other.RowSet],
                HasDelivery || other.HasDelivery);
    }
}
