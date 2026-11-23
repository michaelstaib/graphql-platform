using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;
using VariableBatchRequest = HotChocolate.Transport.VariableBatchRequest;

namespace HotChocolate.Fusion;

public sealed class ArgumentLiteralExtractionTests : FusionTestBase
{
    [Fact]
    public async Task Execute_Should_ShareOperationPlan_When_OperationsDifferOnlyInArgumentLiterals()
    {
        // arrange
        var plans = new List<OperationPlan>();
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, plans, enableExtraction: true);

        // act
        var first = await ExecuteAsync(
            gateway,
            """{ productById(id: "1") { name weight(unit: KG) reviews(first: 1) { body } } }""");
        var second = await ExecuteAsync(
            gateway,
            """{ productById(id: "3") { name weight(unit: LB) reviews(first: 2) { body } } }""");
        var firstAgain = await ExecuteAsync(
            gateway,
            """{ productById(id: "1") { name weight(unit: KG) reviews(first: 1) { body } } }""");

        // assert
        Assert.Same(plans[0], plans[1]);
        Assert.Same(plans[0], plans[2]);
        Assert.Equal(first, firstAgain);
        new[] { first, second }.MatchInlineSnapshots(
        [
            """
            {"productById":{"name":"Table","weight":100,"reviews":[{"body":"Review 1 of product 1"}]}}
            """,
            """
            {"productById":{"name":"Chair","weight":14,"reviews":[{"body":"Review 1 of product 3"},{"body":"Review 2 of product 3"}]}}
            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_PlanEachOperation_When_ExtractionIsDisabled()
    {
        // arrange
        var plans = new List<OperationPlan>();
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, plans, enableExtraction: false);

        // act
        var first = await ExecuteAsync(gateway, """{ productById(id: "1") { name reviews(first: 1) { body } } }""");
        var second = await ExecuteAsync(gateway, """{ productById(id: "3") { name reviews(first: 2) { body } } }""");

        // assert
        Assert.NotSame(plans[0], plans[1]);
        new[] { first, second }.MatchInlineSnapshots(
        [
            """
            {"productById":{"name":"Table","reviews":[{"body":"Review 1 of product 1"}]}}
            """,
            """
            {"productById":{"name":"Chair","reviews":[{"body":"Review 1 of product 3"},{"body":"Review 2 of product 3"}]}}
            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_SendLiteralsAsVariables_When_ExtractionIsEnabled()
    {
        // arrange
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, [], enableExtraction: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            query GetProducts {
              products(first: 2, filter: { namePrefix: "Ta" }, tags: ["wood", "tech"]) {
                name
                weight(unit: LB)
                reviews(first: 1) {
                  body
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
        await MatchSnapshotAsync(gateway, request, result);
    }

    [Fact]
    public async Task Execute_Should_KeepLiteral_When_LiteralContainsVariable()
    {
        // arrange
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, [], enableExtraction: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            query GetProducts($prefix: String, $skipReviews: Boolean!) {
              products(first: 2, filter: { namePrefix: $prefix }) {
                name
                reviews(first: 1) @skip(if: $skipReviews) {
                  body
                }
              }
            }
            """,
            variables: new Dictionary<string, object?> { ["prefix"] = "Ta", ["skipReviews"] = false });

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result);
    }

    [Fact]
    public async Task Execute_Should_ReturnData_When_ClientVariableHasGeneratedVariableName()
    {
        // arrange
        var plans = new List<OperationPlan>();
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, plans, enableExtraction: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            query GetProducts($__fusion_literal_0: Int!) {
              products(first: $__fusion_literal_0) {
                name
                weight(unit: LB)
              }
            }
            """,
            variables: new Dictionary<string, object?> { ["__fusion_literal_0"] = 2 });

        // act
        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        using var response = await result.ReadAsResultAsync(TestContext.Current.CancellationToken);

        // assert
        response.Data.GetRawText().MatchInlineSnapshot(
            """
            {"products":[{"name":"Table","weight":200},{"name":"Tablet","weight":2}]}
            """);
        plans[0].Operation.Definition.MatchInlineSnapshot(
            """
            query GetProducts($__fusion_literal_0: Int!, $__fusion_literal_1: WeightUnit!) {
              products(first: $__fusion_literal_0) {
                name
                weight(unit: $__fusion_literal_1)
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_ResolveIntrospection_When_IntrospectionFieldsHaveArgumentLiterals()
    {
        // arrange
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, [], enableExtraction: true);

        // act
        var result = await ExecuteAsync(
            gateway,
            """
            {
              __type(name: "Review") {
                fields(includeDeprecated: true) {
                  name
                }
              }
              productById(id: "2") {
                __typename
                name
              }
            }
            """);

        // assert
        result.MatchInlineSnapshot(
            """
            {"__type":{"fields":[{"name":"body"}]},"productById":{"__typename":"Product","name":"Tablet"}}
            """);
    }

    [Fact]
    public async Task Execute_Should_ResolveNode_When_NodeIdIsArgumentLiteral()
    {
        // arrange
        using var serverA = CreateSourceSchema(
            "A",
            """
            type Query {
              node(id: ID!): Node @lookup @shareable
              discussionById(discussionId: ID! @is(field: "id")): Discussion @lookup
            }

            interface Node {
              id: ID!
            }

            type Discussion implements Node {
              id: ID!
              title: String!
            }
            """);
        using var serverB = CreateSourceSchema(
            "B",
            """
            type Query {
              node(id: ID!): Node @lookup @shareable
              discussionById(id: ID!): Discussion @lookup @internal
            }

            interface Node {
              id: ID!
            }

            type Discussion implements Node {
              id: ID!
              commentCount: Int!
            }
            """);
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", serverA), ("B", serverB)],
            configureGatewayBuilder: builder =>
                builder.ModifyOptions(options => options.EnableArgumentLiteralExtraction = true));
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            {
              # Discussion:1
              node(id: "RGlzY3Vzc2lvbjox") {
                ... on Discussion {
                  title
                  commentCount
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
        await MatchSnapshotAsync(gateway, request, result);
    }

    [Fact]
    public async Task Execute_Should_DeliverDeferredFragment_When_DeferredFieldHasArgumentLiteral()
    {
        // arrange
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, [], enableExtraction: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new OperationRequest(
            """
            query GetProduct {
              productById(id: "1") {
                name
                ... @defer(label: "reviews") {
                  reviews(first: 2) {
                    body
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
        await MatchSnapshotAsync(gateway, request, result, stableStream: true);
    }

    [Fact]
    public async Task Execute_Should_ReturnResultPerVariableSet_When_VariableBatchHasArgumentLiterals()
    {
        // arrange
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, [], enableExtraction: true);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = new GraphQLHttpRequest(
            new VariableBatchRequest(
                """
                query GetProduct($id: ID!) {
                  productById(id: $id) {
                    name
                    reviews(first: 1) {
                      body
                    }
                  }
                }
                """,
                variables:
                [
                    new Dictionary<string, object?> { ["id"] = "1" },
                    new Dictionary<string, object?> { ["id"] = "3" }
                ]),
            new Uri("http://localhost:5000/graphql"));

        // act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var results = new List<string>();

        await foreach (var result in response.ReadAsResultStreamAsync()
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            using (result)
            {
                results.Add(result.Data.GetRawText());
            }
        }

        // assert
        results.Order(StringComparer.Ordinal).MatchInlineSnapshots(
        [
            """
            {"productById":{"name":"Chair","reviews":[{"body":"Review 1 of product 3"}]}}
            """,
            """
            {"productById":{"name":"Table","reviews":[{"body":"Review 1 of product 1"}]}}
            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_RunMutations_When_MutationFieldsHaveArgumentLiterals()
    {
        // arrange
        using var productsApi = CreateProductsApi();
        using var reviewsApi = CreateReviewsApi();
        using var gateway = await CreateGatewayAsync(productsApi, reviewsApi, [], enableExtraction: true);

        // act
        var result = await ExecuteAsync(
            gateway,
            """
            mutation {
              a: renameProduct(input: { id: "1", name: "Desk" }) {
                name
                reviews(first: 1) {
                  body
                }
              }
              b: renameProduct(input: { id: "2", name: "Phone" }) {
                name
              }
            }
            """);

        // assert
        result.MatchInlineSnapshot(
            """
            {"a":{"name":"Desk","reviews":[{"body":"Review 1 of product 1"}]},"b":{"name":"Phone"}}
            """);
    }

    private Task<Gateway> CreateGatewayAsync(
        TestServer productsApi,
        TestServer reviewsApi,
        List<OperationPlan> plans,
        bool enableExtraction)
        => CreateCompositeSchemaAsync(
            [("products-api", productsApi), ("reviews-api", reviewsApi)],
            configureGatewayBuilder: builder => builder
                .ModifyOptions(options => options.EnableArgumentLiteralExtraction = enableExtraction)
                .UseRequest(
                    (_, next) => context =>
                    {
                        // records the plan each request runs with.
                        lock (plans)
                        {
                            plans.Add(context.GetOperationPlan()!);
                        }

                        return next(context);
                    },
                    after: WellKnownRequestMiddleware.OperationPlanMiddleware,
                    allowMultiple: true));

    private TestServer CreateProductsApi()
        => CreateSourceSchema(
            "products-api",
            builder => builder
                .AddQueryType<ProductsQuery>(d => d.Name("Query"))
                .AddMutationType<ProductsMutation>(d => d.Name("Mutation")));

    private TestServer CreateReviewsApi()
        => CreateSourceSchema(
            "reviews-api",
            builder => builder.AddQueryType<ReviewsQuery>(d => d.Name("Query")));

    private static async Task<string> ExecuteAsync(Gateway gateway, string query)
    {
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            query,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        using var response = await result.ReadAsResultAsync(TestContext.Current.CancellationToken);

        var data = response.Data.ValueKind is JsonValueKind.Undefined
            ? string.Empty
            : response.Data.GetRawText();
        var errors = response.Errors.ValueKind is JsonValueKind.Undefined
            ? string.Empty
            : response.Errors.GetRawText();

        return data + errors;
    }

    public sealed class ProductsQuery
    {
        private static readonly List<Product> s_products =
        [
            new() { Id = "1", Name = "Table", Kilograms = 100, Tags = ["wood"] },
            new() { Id = "2", Name = "Tablet", Kilograms = 1, Tags = ["tech"] },
            new() { Id = "3", Name = "Chair", Kilograms = 7, Tags = ["wood"] }
        ];

        [Lookup]
        public Product? GetProductById([ID] string id)
            => s_products.FirstOrDefault(product => product.Id == id);

        public IEnumerable<Product> GetProducts(int first, ProductFilter? filter = null, List<string>? tags = null)
            => s_products
                .Where(product => filter?.NamePrefix is not { } prefix || product.Name.StartsWith(prefix))
                .Where(product => tags is null || product.Tags.Any(tags.Contains))
                .Take(first);
    }

    public sealed class ProductsMutation
    {
        public Product RenameProduct(RenameProductInput input)
            => new() { Id = input.Id, Name = input.Name, Kilograms = 0, Tags = [] };
    }

    public sealed record RenameProductInput([property: ID] string Id, string Name);

    [GraphQLName("Product")]
    public sealed class Product
    {
        [ID]
        public required string Id { get; init; }

        public required string Name { get; init; }

        [GraphQLIgnore]
        public required double Kilograms { get; init; }

        [GraphQLIgnore]
        public required List<string> Tags { get; init; }

        public double GetWeight(WeightUnit unit)
            => unit is WeightUnit.Kg ? Kilograms : Kilograms * 2;
    }

    public sealed record ProductFilter(string? NamePrefix);

    public enum WeightUnit
    {
        Kg,
        Lb
    }

    public sealed class ReviewsQuery
    {
        [Lookup, Internal]
        public ReviewProduct? GetProductById([ID] string id)
            => new() { Id = id };
    }

    [GraphQLName("Product")]
    public sealed class ReviewProduct
    {
        [ID]
        public required string Id { get; init; }

        public IEnumerable<Review> GetReviews(int first)
            => Enumerable.Range(1, first).Select(number => new Review { Body = $"Review {number} of product {Id}" });
    }

    public sealed class Review
    {
        public required string Body { get; init; }
    }
}
