using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public sealed class KeyHopFailureApolloTests : FusionTestBase
{
    [Fact]
    public async Task Execute_Should_ReportViolationOnlyForTheItemWithoutKey_When_EntityLookupMissesTheKey()
    {
        // arrange
        var a = CreateSourceSchema(
            "a",
            builder => builder
                .AddApolloFederation()
                .AddQueryType<ApolloA.Query>(d => d.Name("Query"))
                .AddType<ApolloA.User>());
        var c = CreateSourceSchema(
            "c",
            builder => builder
                .AddApolloFederation()
                .AddQueryType<ApolloC.Query>(d => d.Name("Query"))
                .AddType<ApolloC.User>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("c", c)],
            enableGlobalObjectIdentification: false,
            includeOperationPlan: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        using var response = await client.PostAsync(
            new OperationRequest("{ nullableUsers { name totalNN } }"),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        using var result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "nullableUsers": [
                  {
                    "name": "user-1",
                    "totalNN": 100
                  },
                  null,
                  {
                    "name": "user-3",
                    "totalNN": 300
                  }
                ]
              },
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableUsers",
                    1,
                    "totalNN"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ]
            }
            """);
    }

    public static class ApolloA
    {
        public sealed class Query
        {
            [Shareable]
            public IEnumerable<User?> GetNullableUsers()
                =>
                [
                    new User { Id = "1", Name = "user-1", Sku = "sku-1" },
                    new User { Id = "2", Name = "user-2", Sku = null },
                    new User { Id = "3", Name = "user-3", Sku = "sku-3" }
                ];
        }

        [Key("id")]
        [Shareable]
        public sealed class User
        {
            [GraphQLType<NonNullType<IdType>>]
            public required string Id { get; init; }

            public required string Name { get; init; }

            [Shareable]
            public string? Sku { get; init; }

            [ReferenceResolver]
            public static User ResolveReference(string id) => new() { Id = id, Name = "user-" + id };
        }
    }

    public static class ApolloC
    {
        public sealed class Query
        {
            [Shareable]
            public User? GetUser() => new() { Id = "1", Sku = "sku-1", TotalNN = 100 };
        }

        [Key("sku")]
        [Shareable]
        public sealed class User
        {
            [GraphQLType<NonNullType<IdType>>]
            [Shareable]
            public required string Id { get; init; }

            public required string Sku { get; init; }

            [GraphQLType<NonNullType<IntType>>]
            public int TotalNN { get; init; }

            [ReferenceResolver]
            public static User ResolveReference(string sku)
                => new() { Id = "c-" + sku, Sku = sku, TotalNN = int.Parse(sku[4..]) * 100 };
        }
    }
}
