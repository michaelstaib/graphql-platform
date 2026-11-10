using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using System.Net;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

[Collection("KeyHopFailure")]
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

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Execute_Should_ReportViolationOfTheFailedDefinition_When_ApolloBatchSkipsArriveInAnyOrder(
        bool enableRequestGrouping,
        bool failureArrivesFirst)
    {
        // arrange
        var a = CreateSourceSchema("a", b => b.AddApolloFederation().AddQueryType<GroupA.Query>(d => d.Name("Query"))
            .AddType<GroupA.User>().AddType<GroupA.Account>());
        var accounts = CreateSourceSchema("accounts", b => b.AddApolloFederation()
            .AddQueryType<GroupAccounts.Query>(d => d.Name("Query")).AddType<GroupAccounts.Account>());
        var gate = new KeyHopFailureTests.ArrivalGate(failureArrivesFirst);
        KeyHopFailureTests.ArrivalGate.Current = gate;
        var b = CreateSourceSchema(
            "b",
            builder => builder.AddApolloFederation().AddQueryType<GroupB.Query>(d => d.Name("Query"))
                .AddType<GroupB.User>(),
            mockHttpResponse: async _ =>
            {
                await gate.BeforeFailureAsync();
                var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                gate.FailureSent();
                return response;
            });
        var e = CreateSourceSchema("e", b => b.AddApolloFederation().AddQueryType<GroupE.Query>(d => d.Name("Query"))
            .AddType<GroupE.Account>());
        var k = CreateSourceSchema("k", b => b.AddApolloFederation().AddQueryType<GroupK.Query>(d => d.Name("Query"))
            .AddType<GroupK.User>().AddType<GroupK.Account>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("accounts", accounts), ("b", b), ("e", e), ("k", k)],
            configureGatewayBuilder: builder =>
            {
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping);
                builder.AddDiagnosticEventListener(_ => new KeyHopFailureTests.ConditionSkipListener());
            },
            enableGlobalObjectIdentification: false,
            includeOperationPlan: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        OperationResult result;

        try
        {
            using var response = await client.PostAsync(
                new OperationRequest(
                    query: "query($s: Boolean!) { nullableUsers { name kayNN } "
                    + "accounts { id code @include(if: $s) kayNN @include(if: $s) } }",
                    variables: new Dictionary<string, object?> { ["s"] = false }),
                new Uri("http://localhost:5000/graphql"),
                TestContext.Current.CancellationToken);
            result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            KeyHopFailureTests.ArrivalGate.Current = null;
        }

        // assert
        using (result)
        {
            result.MatchInlineSnapshot(
                """
                {
                  "data": {
                    "nullableUsers": [
                      null,
                      null
                    ],
                    "accounts": [
                      {
                        "id": "5"
                      }
                    ]
                  },
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "nullableUsers",
                        0,
                        "kayNN"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    },
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "nullableUsers",
                        1,
                        "kayNN"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ]
                }
                """);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Execute_Should_ReportViolationOnlyForTheSkippedDefinition_When_ApolloBatchRunsWithAFailedDependency(
        bool enableRequestGrouping)
    {
        // arrange
        var a = CreateSourceSchema("a", b => b.AddApolloFederation().AddQueryType<GroupA.Query>(d => d.Name("Query"))
            .AddType<GroupA.User>().AddType<GroupA.Account>());
        var accounts = CreateSourceSchema("accounts", b => b.AddApolloFederation()
            .AddQueryType<GroupAccounts.Query>(d => d.Name("Query")).AddType<GroupAccounts.Account>());
        var b = CreateSourceSchema(
            "b",
            builder => builder.AddApolloFederation().AddQueryType<GroupB.Query>(d => d.Name("Query"))
                .AddType<GroupB.User>(),
            mockHttpResponse: _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var e = CreateSourceSchema("e", b => b.AddApolloFederation().AddQueryType<GroupE.Query>(d => d.Name("Query"))
            .AddType<GroupE.Account>());
        var k = CreateSourceSchema("k", b => b.AddApolloFederation().AddQueryType<GroupK.Query>(d => d.Name("Query"))
            .AddType<GroupK.User>().AddType<GroupK.Account>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("accounts", accounts), ("b", b), ("e", e), ("k", k)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            enableGlobalObjectIdentification: false,
            includeOperationPlan: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        // act
        using var response = await client.PostAsync(
            new OperationRequest("{ nullableUsers { name kayNN } accounts { id code kayNN } }"),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        // assert
        using var result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "nullableUsers": [
                  null,
                  null
                ],
                "accounts": [
                  {
                    "id": "5",
                    "code": "code-5",
                    "kayNN": 3
                  }
                ]
              },
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableUsers",
                    0,
                    "kayNN"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                },
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableUsers",
                    1,
                    "kayNN"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ]
            }
            """);
    }

    public static class GroupA
    {
        public sealed class Query
        {
            [Shareable]
            public IEnumerable<User?> GetNullableUsers()
                => [new User { Id = "2", Name = "user-2" }, new User { Id = "3", Name = "user-3" }];
        }

        [Key("id")]
        [Shareable]
        public sealed class User
        {
            [GraphQLType<NonNullType<IdType>>]
            public required string Id { get; init; }

            public required string Name { get; init; }

            [ReferenceResolver]
            public static User ResolveReference(string id) => new() { Id = id, Name = "user-" + id };
        }

        [Key("id")]
        [Shareable]
        public sealed class Account
        {
            [GraphQLType<NonNullType<IdType>>]
            public required string Id { get; init; }

            [ReferenceResolver]
            public static Account ResolveReference(string id) => new() { Id = id };
        }
    }

    public static class GroupAccounts
    {
        public sealed class Query
        {
            [Shareable]
            public async Task<IEnumerable<Account?>> GetAccounts()
            {
                await (KeyHopFailureTests.ArrivalGate.Current?.BeforeAccountsAsync() ?? Task.CompletedTask);
                return [new Account { Id = "5" }];
            }
        }

        [Key("id")]
        [Shareable]
        public sealed class Account
        {
            [GraphQLType<NonNullType<IdType>>]
            public required string Id { get; init; }

            [ReferenceResolver]
            public static Account ResolveReference(string id) => new() { Id = id };
        }
    }

    public static class GroupB
    {
        public sealed class Query
        {
            [Shareable]
            public int? GetNoop() => null;
        }

        [Key("id")]
        [Shareable]
        public sealed class User
        {
            [GraphQLType<NonNullType<IdType>>]
            public required string Id { get; init; }

            [Shareable]
            public string? Sku { get; init; }

            [ReferenceResolver]
            public static User ResolveReference(string id) => new() { Id = id, Sku = "sku-" + id };
        }
    }

    public static class GroupE
    {
        public sealed class Query
        {
            [Shareable]
            public int? GetNoop2() => null;
        }

        [Key("id")]
        [Shareable]
        public sealed class Account
        {
            [GraphQLType<NonNullType<IdType>>]
            public required string Id { get; init; }

            [Shareable]
            public string? Code { get; init; }

            [ReferenceResolver]
            public static Account ResolveReference(string id) => new() { Id = id, Code = "code-" + id };
        }
    }

    public static class GroupK
    {
        public sealed class Query
        {
            [Shareable]
            public int? GetNoop3() => null;
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
            public int KayNN { get; init; }

            [ReferenceResolver]
            public static User ResolveReference(string sku) => new() { Id = "k-" + sku, Sku = sku, KayNN = 2 };
        }

        [Key("code")]
        [Shareable]
        public sealed class Account
        {
            [GraphQLType<NonNullType<IdType>>]
            [Shareable]
            public required string Id { get; init; }

            public required string Code { get; init; }

            [GraphQLType<NonNullType<IntType>>]
            public int KayNN { get; init; }

            [ReferenceResolver]
            public static Account ResolveReference(string code) => new() { Id = "k-" + code, Code = code, KayNN = 3 };
        }
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
