using System.Text;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotChocolate.Fusion;

public class HeaderPropagationTests : FusionTestBase
{
    private const string TenantHeader = "X-Tenant";

    [Fact]
    public async Task Execute_Should_ForwardHeaderOfEachRequest_When_HeaderIsPropagated()
    {
        // arrange
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureGatewayBuilder: b => b.AddHeaderPropagation(TenantHeader),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var results = new List<string>();

        foreach (var tenant in new[] { "first", "second", "third" })
        {
            results.Add(await ExecuteAsync(client, "{ tenant }", tenant));
        }

        // assert
        results.MatchInlineSnapshots(
        [
            """{"data":{"tenant":"first"}}""",
            """{"data":{"tenant":"second"}}""",
            """{"data":{"tenant":"third"}}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_NotForwardHeader_When_HeaderIsNotPropagated()
    {
        // arrange
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureGatewayBuilder: b => b.AddHeaderPropagation("X-Other"),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ tenant }", "first");

        // assert
        result.MatchInlineSnapshot("""{"data":{"tenant":"none"}}""");
    }

    [Fact]
    public async Task Execute_Should_NotAddHeader_When_RequestHasNoSuchHeader()
    {
        // arrange
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureGatewayBuilder: b => b.AddHeaderPropagation(TenantHeader),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ tenant }", tenant: null);

        // assert
        result.MatchInlineSnapshot("""{"data":{"tenant":"none"}}""");
    }

    [Fact]
    public async Task Execute_Should_KeepHeaderOfOnBeforeSend_When_HeaderIsAlsoPropagated()
    {
        // arrange
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureGatewayBuilder: b => b
                .AddHeaderPropagation(TenantHeader)
                .AddHttpClientConfiguration(
                    "A",
                    new Uri("http://localhost:5000/graphql"),
                    onBeforeSend: (_, _, request) => request.Headers.Add(TenantHeader, "explicit")),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ tenant }", "first");

        // assert
        result.MatchInlineSnapshot("""{"data":{"tenant":"explicit"}}""");
    }

    [Fact]
    public async Task Execute_Should_KeepDefaultHeaderOfHttpClient_When_HeaderIsAlsoPropagated()
    {
        // arrange
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureServices: services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services
                    .AddHttpClient("A")
                    .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add(TenantHeader, "default"))
                    .ConfigurePrimaryHttpMessageHandler(() => server.CreateHandler());
            },
            configureGatewayBuilder: b => b.AddHeaderPropagation(TenantHeader),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ tenant }", "first");

        // assert
        result.MatchInlineSnapshot("""{"data":{"tenant":"default"}}""");
    }

    [Fact]
    public async Task Execute_Should_ForwardHeader_When_HttpClientHasOtherDefaultHeader()
    {
        // arrange
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureServices: services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services
                    .AddHttpClient("A")
                    .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("X-Other", "default"))
                    .ConfigurePrimaryHttpMessageHandler(() => server.CreateHandler());
            },
            configureGatewayBuilder: b => b.AddHeaderPropagation(TenantHeader),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ tenant }", "first");

        // assert
        result.MatchInlineSnapshot("""{"data":{"tenant":"first"}}""");
    }

    [Fact]
    public void AddHeaderPropagation_Should_Throw_When_HeaderDescribesBody()
    {
        // arrange
        var builder = new ServiceCollection().AddGraphQLGatewayServer();

        // act
        var exception = Record.Exception(() => builder.AddHeaderPropagation(TenantHeader, "Content-Length"));

        // assert
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Equal(
            "The header 'Content-Length' describes the body or the connection of a single request "
            + "and cannot be copied to source schema requests. (Parameter 'headerNames')",
            argumentException.Message);
    }

    [Fact]
    public async Task AddHeaderPropagation_Should_UseNamesOfTheCall_When_ArrayChangesAfterwards()
    {
        // arrange
        string[] headerNames = [TenantHeader];
        using var server = CreateSourceSchema(
            "A",
            b => b.AddQueryType<Query>(),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureGatewayBuilder: b =>
            {
                b.AddHeaderPropagation(headerNames);
                headerNames[0] = "X-Other";
            },
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ tenant }", "first");

        // assert
        result.MatchInlineSnapshot("""{"data":{"tenant":"first"}}""");
    }

    [Fact]
    public async Task Execute_Should_ForwardHeaderToLookups_When_HeaderIsPropagated()
    {
        // arrange
        using var productsApi = CreateSourceSchema(
            "products-api",
            b => b.AddQueryType<ProductsQuery>(d => d.Name("Query")));
        using var tenantsApi = CreateSourceSchema(
            "tenants-api",
            b => b.AddQueryType<TenantsQuery>(d => d.Name("Query")),
            configureServices: s => s.AddHttpContextAccessor());
        using var gateway = await CreateCompositeSchemaAsync(
            [("products-api", productsApi), ("tenants-api", tenantsApi)],
            configureGatewayBuilder: b => b.AddHeaderPropagation(TenantHeader),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        // act
        var result = await ExecuteAsync(client, "{ products { tenant } }", "first");

        // assert
        result.MatchInlineSnapshot(
            """{"data":{"products":[{"tenant":"first"},{"tenant":"first"}]}}""");
    }

    private static async Task<string> ExecuteAsync(HttpClient client, string query, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql");

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        request.Content = new StringContent(
            $$"""{ "query": "{{query}}" }""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static string GetTenant(IHttpContextAccessor httpContextAccessor)
        => httpContextAccessor.HttpContext?.Request.Headers[TenantHeader].ToString() is { Length: > 0 } value
            ? value
            : "none";

    public sealed class Query
    {
        public string GetTenant([Service] IHttpContextAccessor httpContextAccessor)
            => HeaderPropagationTests.GetTenant(httpContextAccessor);
    }

    public sealed class ProductsQuery
    {
        public List<ProductsProduct> GetProducts()
            => [new() { Id = "1" }, new() { Id = "2" }];

        [Lookup, Internal]
        public ProductsProduct? GetProductById([ID] string id)
            => new() { Id = id };
    }

    [GraphQLName("Product")]
    public sealed class ProductsProduct
    {
        [ID]
        public required string Id { get; init; }
    }

    public sealed class TenantsQuery
    {
        [Lookup]
        public TenantsProduct? GetProductById([ID] string id)
            => new() { Id = id };
    }

    [GraphQLName("Product")]
    public sealed class TenantsProduct
    {
        [ID]
        public required string Id { get; init; }

        public string GetTenant([Service] IHttpContextAccessor httpContextAccessor)
            => HeaderPropagationTests.GetTenant(httpContextAccessor);
    }
}
