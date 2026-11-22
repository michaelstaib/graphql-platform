using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public sealed class ConditionalLookupKeyTests : FusionTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Field_Should_BeResolved_When_LookupKeyIsFetchedForSkippedSiblingField(bool skip)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: false);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              products { upc eur @skip(if: $a) rating }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, skip.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Field_Should_BeResolved_When_LookupKeyIsFetchedForNotIncludedSiblingField(bool include)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: false);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              products { upc eur @include(if: $a) rating }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = include });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, include.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyField_Should_BeResolved_When_ItIsFetchedForSkippedSiblingField(bool skip)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: false);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              products { upc eur @skip(if: $a) id }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, skip.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Field_Should_BeResolved_When_LookupKeyIsFetchedForSkippedSiblingFieldOfSingleEntity(bool skip)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: false);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              product { upc eur @skip(if: $a) rating }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, skip.ToString());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task LookupKey_Should_StayInConditionalStep_When_EverySelectionOfLookupCarriesTheCondition(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: false);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              products { upc eur @skip(if: $a) rating @skip(if: $a) @include(if: $b) }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip, ["b"] = include });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skip}_{include}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Lookup_Should_NotBeRequested_When_EverySelectionOfLookupIsSkipped(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: false);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              products { upc eur @skip(if: $a) @include(if: $b) rating @skip(if: $a) }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip, ["b"] = include });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skip}_{include}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Field_Should_BeResolved_When_ItsRequirementIsAFieldOfAConditionalLookup(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateProductGatewayAsync(includeStockSource: true);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              products { upc eur @skip(if: $a) rating @skip(if: $a) @include(if: $b) stock }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip, ["b"] = include });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skip}_{include}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Field_Should_BeResolved_When_KeyOfItsRequirementLookupIsFetchedForSkippedSiblingField(
        bool skip)
    {
        // arrange
        using var serverA = CreateSourceSchema(
            "A",
            """
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """);
        using var serverD = CreateSourceSchema(
            "D",
            """
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") @key(fields: "sku") {
              id: ID!
              sku: ID!
              weight: Int!
              total: Int!
            }
            """);
        using var serverB = CreateSourceSchema(
            "B",
            """
            type Query {
              userBySku(sku: ID!): User @lookup @internal
            }

            type User @key(fields: "sku") {
              sku: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);
        using var gateway = await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB)]);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              user { shipping tax @skip(if: $a) }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, skip.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Field_Should_BeResolved_When_SiblingFieldOfSameSourceRunsUnderOppositeCondition(bool include)
    {
        // arrange
        using var gateway = await CreateCurrencyGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($v: Boolean!) {
              product { eur @include(if: $v) usd @skip(if: $v) }
            }
            """,
            variables: new Dictionary<string, object?> { ["v"] = include });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, include.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Field_Should_BeResolved_When_SiblingFieldUnderOppositeConditionComesFirst(bool include)
    {
        // arrange
        using var gateway = await CreateCurrencyGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($v: Boolean!) {
              product { usd @skip(if: $v) eur @include(if: $v) }
            }
            """,
            variables: new Dictionary<string, object?> { ["v"] = include });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, include.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupKey_Should_StayInParentStep_When_ConditionSitsOnParentField(bool skip)
    {
        // arrange
        using var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<OrderSchemas.A.Query>(d => d.Name("Query")));
        using var serverD = CreateSourceSchema(
            "D",
            builder => builder.AddQueryType<OrderSchemas.D.Query>(d => d.Name("Query")));
        using var serverE = CreateSourceSchema(
            "E",
            builder => builder.AddQueryType<OrderSchemas.E.Query>(d => d.Name("Query")));
        using var gateway = await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("E", serverE)]);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              user { order @skip(if: $a) { item { price } } }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skip });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, skip.ToString());
    }

    private async Task<Gateway> CreateProductGatewayAsync(bool includeStockSource)
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<ProductSchemas.A.Query>(d => d.Name("Query")));
        var serverB = CreateSourceSchema(
            "B",
            builder => builder.AddQueryType<ProductSchemas.B.Query>(d => d.Name("Query")));
        var serverS = CreateSourceSchema(
            "S",
            builder => builder.AddQueryType<ProductSchemas.S.Query>(d => d.Name("Query")));
        var serverT = CreateSourceSchema(
            "T",
            builder => builder.AddQueryType<ProductSchemas.T.Query>(d => d.Name("Query")));

        if (!includeStockSource)
        {
            return await CreateCompositeSchemaAsync([("A", serverA), ("B", serverB), ("S", serverS), ("T", serverT)]);
        }

        var serverU = CreateSourceSchema(
            "U",
            builder => builder.AddQueryType<ProductSchemas.U.Query>(d => d.Name("Query")));

        return await CreateCompositeSchemaAsync(
            [("A", serverA), ("B", serverB), ("S", serverS), ("T", serverT), ("U", serverU)]);
    }

    private async Task<Gateway> CreateCurrencyGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<CurrencySchemas.A.Query>(d => d.Name("Query")));
        var serverB = CreateSourceSchema(
            "B",
            builder => builder.AddQueryType<CurrencySchemas.B.Query>(d => d.Name("Query")));

        return await CreateCompositeSchemaAsync([("A", serverA), ("B", serverB)]);
    }

    private static class ProductSchemas
    {
        public static class A
        {
            public class Query
            {
                public List<Product> GetProducts() => [new("1"), new("2")];

                public Product? GetProduct() => new("1");

                [Lookup]
                public Product? GetProductByUpc(string upc) => new(upc);
            }

            [EntityKey("upc")]
            public record Product(string Upc)
            {
                public int? GetEur([Require("price(cur: \"EUR\")")] int? price) => price;
            }
        }

        public static class B
        {
            public class Query
            {
                [Lookup, Internal]
                public Product? GetProductById(int id) => new(id);
            }

            [EntityKey("id")]
            public record Product([property: Shareable] int Id)
            {
                public int? GetPrice(string? cur) => cur == "EUR" ? Id * 80 : Id * 90;
            }
        }

        public static class S
        {
            public class Query
            {
                [Lookup, Internal]
                public Product? GetProductByUpc(string upc) => new(upc);
            }

            [EntityKey("upc")]
            public record Product(string Upc)
            {
                [Shareable]
                public int Id => int.Parse(Upc) * 11;
            }
        }

        public static class T
        {
            public class Query
            {
                [Lookup, Internal]
                public Product? GetProductById(int id) => new(id);
            }

            [EntityKey("id")]
            public record Product([property: Shareable] int Id)
            {
                public int Rating => Id + 3;
            }
        }

        public static class U
        {
            public class Query
            {
                [Lookup, Internal]
                public Product? GetProductById(int id) => new(id);
            }

            [EntityKey("id")]
            public record Product([property: Shareable] int Id)
            {
                public int? GetStock([Require("rating")] int? rating) => rating * 2;
            }
        }
    }

    private static class CurrencySchemas
    {
        public static class A
        {
            public class Query
            {
                public Product GetProduct() => new("1");

                [Lookup, Internal]
                public Product? GetProductByUpc(string upc) => new(upc);
            }

            [EntityKey("upc")]
            public record Product(string Upc)
            {
                public int? GetEur([Require("price(cur: \"EUR\")")] int? price) => price;

                public int? GetUsd([Require("price(cur: \"USD\")")] int? price) => price;
            }
        }

        public static class B
        {
            public class Query
            {
                [Lookup, Internal]
                public Product? GetProductByUpc(string upc) => new(upc);
            }

            [EntityKey("upc")]
            public record Product(string Upc)
            {
                public int? GetPrice(string? cur) => cur == "EUR" ? 80 : cur == "USD" ? 90 : 100;
            }
        }
    }

    private static class OrderSchemas
    {
        public static class A
        {
            public class Query
            {
                public User GetUser() => new(1);
            }

            public record User(int Id)
            {
                public Order Order => new(Id + 1);
            }

            [EntityKey("id")]
            public record Order(int Id);
        }

        public static class D
        {
            public class Query
            {
                [Lookup, Internal]
                public Order? GetOrderById(int id) => new(id);
            }

            [EntityKey("id")]
            public record Order(int Id)
            {
                public Item Item => new(Id + 1);
            }

            [EntityKey("id")]
            public record Item(int Id);
        }

        public static class E
        {
            public class Query
            {
                [Lookup, Internal]
                public Item? GetItemById(int id) => new(id);
            }

            [EntityKey("id")]
            public record Item(int Id)
            {
                public int Price => Id * 7;
            }
        }
    }
}
