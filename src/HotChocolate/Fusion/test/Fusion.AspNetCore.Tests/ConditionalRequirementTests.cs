using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public sealed class ConditionalRequirementTests : FusionTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Requirement_Should_BeResolved_When_SiblingFieldWithRequirementIsSkipped(bool skip)
    {
        // arrange
        using var gateway = await CreateUserGatewayAsync();

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
    public async Task Requirement_Should_BeResolved_When_SkippedSiblingFieldWithRequirementComesFirst(bool skip)
    {
        // arrange
        using var gateway = await CreateUserGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              user { tax @skip(if: $a) shipping }
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
    public async Task Requirement_Should_BeResolved_When_SiblingFieldWithRequirementIsNotIncluded(bool include)
    {
        // arrange
        using var gateway = await CreateUserGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              user { shipping tax @include(if: $a) }
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
    public async Task Requirement_Should_BeResolved_When_SiblingFieldWithRequirementIsSkippedByFragment(bool skip)
    {
        // arrange
        using var gateway = await CreateUserGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              user {
                shipping
                ... @skip(if: $a) { tax }
              }
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
    public async Task Requirement_Should_BeResolved_When_SiblingFieldWithRequirementIsSkippedInList(bool skip)
    {
        // arrange
        using var gateway = await CreateUserGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              users { shipping tax @skip(if: $a) }
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
    public async Task Requirement_Should_BeResolved_When_FieldsWithRequirementsAreSkippedByDifferentVariables(
        bool skipShipping,
        bool skipTax)
    {
        // arrange
        using var gateway = await CreateUserGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user { shipping @skip(if: $b) tax @skip(if: $a) }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skipTax, ["b"] = skipShipping });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skipShipping}_{skipTax}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Requirement_Should_BeResolved_When_ConditionSitsOnAncestorField(
        bool skipUser,
        bool skipTax)
    {
        // arrange
        using var gateway = await CreateOrderGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user @skip(if: $a) { order { shipping tax @skip(if: $b) } }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skipUser, ["b"] = skipTax });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skipUser}_{skipTax}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Requirement_Should_BeResolved_When_SourceOfRequirementHasNoLookupForTheEntity(bool skip)
    {
        // arrange
        using var gateway = await CreateParentLookupGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              user { order { tax @skip(if: $a) shipping } }
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
    public async Task Requirement_Should_BeResolved_When_InterfaceSelectionIsPlannedPerConcreteType(
        bool skipUser,
        bool skipTax)
    {
        // arrange
        using var gateway = await CreateItemGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user @skip(if: $a) { item { shipping tax @skip(if: $b) } }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skipUser, ["b"] = skipTax });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skipUser}_{skipTax}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Requirement_Should_BeResolved_When_SiblingFieldIsSkippedBelowSharedInterfaceField(
        bool skipUser,
        bool skipTax)
    {
        // arrange
        using var gateway = await CreateSharedItemGatewayAsync();

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user @skip(if: $a) { item { shipping tax @skip(if: $b) } }
            }
            """,
            variables: new Dictionary<string, object?> { ["a"] = skipUser, ["b"] = skipTax });
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        await MatchSnapshotAsync(gateway, request, result, $"{skipUser}_{skipTax}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Requirement_Should_BeResolved_When_ConditionSitsOnInterfaceTypeConditionBelowNode(bool skip)
    {
        // arrange
        using var serverA = CreateSourceSchema(
            "A",
            """
            type Query {
              node(id: ID!): Node @lookup
            }

            interface Node {
              id: ID!
            }

            type Product implements Node @key(fields: "id") {
              id: ID!
              name: String
            }
            """);
        using var serverD = CreateSourceSchema(
            "D",
            """
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
              weight: Int!
              total: Int!
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """);
        using var serverB = CreateSourceSchema(
            "B",
            """
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);
        using var gateway = await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB)]);

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!) {
              # Product:1
              node(id: "UHJvZHVjdDox") { ... on Item @skip(if: $a) { weight shipping } }
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
    public async Task Field_Should_BeResolved_When_RequirementIsSharedWithSkippedFieldOfOtherSource(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateTwoRequiringSourcesGatewayAsync(TwoRequiringSourcesUserSchema, "User");

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user { tax @skip(if: $a) label shipping @skip(if: $a) @include(if: $b) }
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
    public async Task Field_Should_BeResolved_When_RequirementIsSharedWithSkippedFieldsOfBothSources(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateTwoRequiringSourcesGatewayAsync(TwoRequiringSourcesUserSchema, "User");

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user { label shipping @skip(if: $a) @include(if: $b) tax @skip(if: $a) rating }
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Field_Should_BeResolved_When_RequirementIsSharedWithSkippedFieldOfOtherSourceInList(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateTwoRequiringSourcesGatewayAsync(TwoRequiringSourcesUserSchema, "User");

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              users { tax @skip(if: $a) label shipping @skip(if: $a) @include(if: $b) }
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Field_Should_BeResolved_When_RequirementIsSharedWithSkippedFieldOfOtherSourceBelowChildEntity(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateTwoRequiringSourcesGatewayAsync(
            """
            type Query {
              user: User!
            }

            type User {
              id: ID!
              order: Order!
            }

            type Order @key(fields: "id") {
              id: ID!
            }
            """,
            "Order");

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              user { order { label tax @skip(if: $a) shipping @skip(if: $a) @include(if: $b) } }
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Field_Should_BeResolved_When_RequirementIsSharedWithSkippedFieldOfOtherSourcePerConcreteType(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateTwoRequiringSourcesGatewayAsync(
            """
            type Query {
              thing: Thing!
            }

            interface Thing {
              id: ID!
            }

            type Alpha implements Thing @key(fields: "id") {
              id: ID!
              item: Product!
            }

            type Beta implements Thing @key(fields: "id") {
              id: ID!
              item: Product!
            }

            type Product @key(fields: "id") {
              id: ID!
            }
            """,
            "Product");

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              thing {
                ... on Alpha { item { label tax @skip(if: $a) shipping @skip(if: $a) @include(if: $b) } }
                ... on Beta { item { label shipping } }
              }
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Field_Should_BeResolved_When_RequirementIsSharedWithSkippedFieldOfOtherSourceBelowNode(
        bool skip,
        bool include)
    {
        // arrange
        using var gateway = await CreateTwoRequiringSourcesGatewayAsync(
            """
            type Query {
              node(id: ID!): Node @lookup
            }

            interface Node {
              id: ID!
            }

            type Product implements Node @key(fields: "id") {
              id: ID!
            }
            """,
            "Product");

        // act
        var request = new OperationRequest(
            """
            query testQuery($a: Boolean!, $b: Boolean!) {
              # Product:1
              node(id: "UHJvZHVjdDox") {
                ... on Product { label tax @skip(if: $a) shipping @skip(if: $a) @include(if: $b) }
              }
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

    private const string TwoRequiringSourcesUserSchema =
        """
        type Query {
          user: User!
          users: [User!]!
          userById(id: ID!): User @lookup @internal
        }

        type User @key(fields: "id") {
          id: ID!
        }
        """;

    private async Task<Gateway> CreateTwoRequiringSourcesGatewayAsync(string sourceA, string entityType)
    {
        var lookup = char.ToLowerInvariant(entityType[0]) + entityType[1..] + "ById";
        var serverA = CreateSourceSchema("A", sourceA);
        var serverD = CreateSourceSchema(
            "D",
            $$"""
            type Query {
              {{lookup}}(id: ID!): {{entityType}} @lookup @internal
            }

            type {{entityType}} @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """);
        var serverB = CreateSourceSchema(
            "B",
            $$"""
            type Query {
              {{lookup}}(id: ID!): {{entityType}} @lookup @internal
            }

            type {{entityType}} @key(fields: "id") {
              id: ID!
              label: String
              shipping(weight: Int @require(field: "weight")): Int
            }
            """);
        var serverC = CreateSourceSchema(
            "C",
            $$"""
            type Query {
              {{lookup}}(id: ID!): {{entityType}} @lookup @internal
            }

            type {{entityType}} @key(fields: "id") {
              id: ID!
              rating: Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        return await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB), ("C", serverC)]);
    }

    private async Task<Gateway> CreateItemGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<ItemSchemas.A.Query>(d => d.Name("Query")));
        var serverD = CreateSourceSchema(
            "D",
            builder => builder
                .AddQueryType<ItemSchemas.D.Query>(d => d.Name("Query"))
                .AddType<ItemSchemas.D.Product>());
        var serverB = CreateSourceSchema(
            "B",
            builder => builder
                .AddQueryType<ItemSchemas.B.Query>(d => d.Name("Query"))
                .AddType<ItemSchemas.B.IItem>()
                .AddType<ItemSchemas.B.Product>());

        return await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB)]);
    }

    private async Task<Gateway> CreateSharedItemGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder
                .AddQueryType<SharedItemSchemas.A.Query>(d => d.Name("Query"))
                .AddType<SharedItemSchemas.A.Product>());
        var serverD = CreateSourceSchema(
            "D",
            builder => builder
                .AddQueryType<SharedItemSchemas.D.Query>(d => d.Name("Query"))
                .AddType<SharedItemSchemas.D.Product>());
        var serverB = CreateSourceSchema(
            "B",
            builder => builder
                .AddQueryType<ItemSchemas.B.Query>(d => d.Name("Query"))
                .AddType<ItemSchemas.B.IItem>()
                .AddType<ItemSchemas.B.Product>());

        return await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB)]);
    }

    private async Task<Gateway> CreateOrderGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<OrderSchemas.A.Query>(d => d.Name("Query")));
        var serverD = CreateSourceSchema(
            "D",
            builder => builder.AddQueryType<OrderSchemas.D.Query>(d => d.Name("Query")));
        var serverC = CreateSourceSchema(
            "C",
            builder => builder.AddQueryType<OrderSchemas.C.Query>(d => d.Name("Query")));
        var serverB = CreateSourceSchema(
            "B",
            builder => builder.AddQueryType<OrderSchemas.B.Query>(d => d.Name("Query")));

        return await CreateCompositeSchemaAsync(
            [("A", serverA), ("D", serverD), ("C", serverC), ("B", serverB)]);
    }

    private async Task<Gateway> CreateParentLookupGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<ParentLookupSchemas.A.Query>(d => d.Name("Query")));
        var serverD = CreateSourceSchema(
            "D",
            builder => builder.AddQueryType<ParentLookupSchemas.D.Query>(d => d.Name("Query")));
        var serverB = CreateSourceSchema(
            "B",
            builder => builder.AddQueryType<ParentLookupSchemas.B.Query>(d => d.Name("Query")));

        return await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB)]);
    }

    private async Task<Gateway> CreateUserGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "A",
            builder => builder.AddQueryType<UserSchemas.A.Query>(d => d.Name("Query")));
        var serverD = CreateSourceSchema(
            "D",
            builder => builder.AddQueryType<UserSchemas.D.Query>(d => d.Name("Query")));
        var serverB = CreateSourceSchema(
            "B",
            builder => builder.AddQueryType<UserSchemas.B.Query>(d => d.Name("Query")));

        return await CreateCompositeSchemaAsync([("A", serverA), ("D", serverD), ("B", serverB)]);
    }

    private static class UserSchemas
    {
        public static class A
        {
            public class Query
            {
                public User GetUser() => new(1);

                public List<User> GetUsers() => [new(1), new(2)];

                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id);
        }

        public static class D
        {
            public class Query
            {
                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                public int Weight => Id * 10;

                public int Total => Id * 100;
            }
        }

        public static class B
        {
            public class Query
            {
                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                public int? GetShipping([Require("weight")] int? weight) => weight * 101;

                public int? GetTax([Require("total")] int? total) => total + 7;
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

                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id);
        }

        public static class D
        {
            public class Query
            {
                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);

                [Lookup, Internal]
                public Order? GetOrderById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                public Order Order => new(Id);
            }

            [EntityKey("id")]
            public record Order(int Id)
            {
                public int Weight => Id * 10;

                public int Total => Id * 100;
            }
        }

        public static class C
        {
            public class Query
            {
                [Lookup, Internal]
                public Order? GetOrderById(int id) => new(id);
            }

            [EntityKey("id")]
            [EntityKey("sku")]
            public record Order(int Id)
            {
                public int Sku => Id + 1000;
            }
        }

        public static class B
        {
            public class Query
            {
                [Lookup, Internal]
                public Order? GetOrderBySku(int sku) => new(sku);
            }

            [EntityKey("sku")]
            public record Order(int Sku)
            {
                public int? GetShipping([Require("weight")] int? weight) => weight * 101;

                public int? GetTax([Require("total")] int? total) => total + 7;
            }
        }
    }

    private static class ParentLookupSchemas
    {
        public static class A
        {
            public class Query
            {
                public User GetUser() => new(1);

                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                [Shareable]
                public Order Order => new(Id);
            }

            [EntityKey("id")]
            public record Order(int Id);
        }

        public static class D
        {
            public class Query
            {
                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                [Shareable]
                public Order Order => new(Id);
            }

            [EntityKey("id")]
            public record Order(int Id)
            {
                public int Weight => Id * 10;

                public int Total => Id * 100;
            }
        }

        public static class B
        {
            public class Query
            {
                [Lookup, Internal]
                public Order? GetOrderById(int id) => new(id);
            }

            [EntityKey("id")]
            public record Order(int Id)
            {
                public int? GetShipping([Require("weight")] int? weight) => weight * 101;

                public int? GetTax([Require("total")] int? total) => total + 7;
            }
        }
    }

    private static class ItemSchemas
    {
        public static class A
        {
            public class Query
            {
                public User GetUser() => new(1);

                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id);
        }

        public static class D
        {
            public class Query
            {
                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);

                [Lookup, Internal]
                public Product? GetProductById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                public IItem Item => new Product(Id);
            }

            [InterfaceType("Item")]
            public interface IItem
            {
                int Id { get; }

                int Weight { get; }

                int Total { get; }
            }

            [EntityKey("id")]
            public record Product(int Id) : IItem
            {
                public int Weight => Id * 10;

                public int Total => Id * 100;
            }
        }

        public static class B
        {
            public class Query
            {
                [Lookup, Internal]
                public Product? GetProductById(int id) => new(id);
            }

            [InterfaceType("Item")]
            public interface IItem
            {
                int Id { get; }

                int? GetShipping([Require("weight")] int? weight);

                int? GetTax([Require("total")] int? total);
            }

            [EntityKey("id")]
            public record Product(int Id) : IItem
            {
                public int? GetShipping([Require("weight")] int? weight) => weight * 101;

                public int? GetTax([Require("total")] int? total) => total + 7;
            }
        }
    }

    private static class SharedItemSchemas
    {
        public static class A
        {
            public class Query
            {
                public User GetUser() => new(1);

                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                [Shareable]
                public IItem Item => new Product(Id);
            }

            [InterfaceType("Item")]
            public interface IItem
            {
                int Id { get; }
            }

            [EntityKey("id")]
            public record Product(int Id) : IItem;
        }

        public static class D
        {
            public class Query
            {
                [Lookup, Internal]
                public User? GetUserById(int id) => new(id);

                [Lookup, Internal]
                public Product? GetProductById(int id) => new(id);
            }

            [EntityKey("id")]
            public record User(int Id)
            {
                [Shareable]
                public IItem Item => new Product(Id);
            }

            [InterfaceType("Item")]
            public interface IItem
            {
                int Id { get; }

                int Weight { get; }

                int Total { get; }
            }

            [EntityKey("id")]
            public record Product(int Id) : IItem
            {
                public int Weight => Id * 10;

                public int Total => Id * 100;
            }
        }
    }
}
