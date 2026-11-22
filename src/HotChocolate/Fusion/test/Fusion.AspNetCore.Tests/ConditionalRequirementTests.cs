using HotChocolate.Transport;
using HotChocolate.Transport.Http;
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
}
