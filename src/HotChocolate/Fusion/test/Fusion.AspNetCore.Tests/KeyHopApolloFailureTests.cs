using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public sealed class KeyHopApolloFailureTests : FusionTestBase
{
    private static readonly ConcurrentQueue<string> s_calls = new();

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceAtTheDependentField_When_MiddleEntityResolverThrows()
    {
        // arrange
        using var gateway = await CreateGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, [2, 98, 3], [98, 2, 3], [2, 3, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3]
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [98, 2, 3]
            data: {"users":[{"name":"user-98","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            errors: [{"message":"B hop failed for 98","path":["users",0,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98]
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-98","total":null}]}
            errors: [{"message":"B hop failed for 98","path":["users",2,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheItemWithoutKey_When_MiddleEntityResolverReturnsNull()
    {
        // arrange
        using var gateway = await CreateGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, [2, 97, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 3]
            data: {"users":[{"name":"user-2","total":2},{"name":"user-97","total":null},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceAtTheDependentField_When_LastEntityResolverThrows()
    {
        // arrange
        using var gateway = await CreateGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, [2, 96, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 96, 3]
            data: {"users":[{"name":"user-2","total":2},{"name":"user-96","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"C hop failed for sku-96","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:96 C:sku-2 C:sku-3 C:sku-96

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneErrorPerAffectedPath_When_MiddleEntitiesRequestFailsWithServerError()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            mockMiddleHop: _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // act
        var result = await ExecuteAsync(gateway, [2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3]
            data: {"users":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"Unexpected Execution Error","path":["users",1,"total"]}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneErrorPerAffectedPath_When_LastEntitiesRequestFailsWithServerError()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            mockLastHop: _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // act
        var result = await ExecuteAsync(gateway, [2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3]
            data: {"users":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"Unexpected Execution Error","path":["users",1,"total"]}]
            lookups: B:2 B:3

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportTheErrorOnTheNullEntity_When_ANullEntityAndAThrowShareABatch()
    {
        // arrange
        using var gateway = await CreateGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, [97, 98, 2]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [97, 98, 2]
            data: {"users":[{"name":"user-97","total":null},{"name":"user-98","total":null},{"name":"user-2","total":2}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:97 B:98 C:sku-2

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportEachError_When_TwoEntityResolversThrowDifferentErrors()
    {
        // arrange
        using var gateway = await CreateGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, [98, 95, 2]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [98, 95, 2]
            data: {"users":[{"name":"user-98","total":null},{"name":"user-95","total":null},{"name":"user-2","total":2}]}
            errors: [{"message":"B hop failed for 98","path":["users",0,"total"]},{"message":"B hop failed for 95","path":["users",1,"total"]}]
            lookups: B:2 B:95 B:98 C:sku-2

            """);
    }

    [Fact]
    public async Task Execute_Should_AttributeAListError_When_ExactlyOneEntityIsNull()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            mockLastHop: _ => Task.FromResult(CreateJsonResponse(
                """
                {"data":{"_entities":[{"total":2},null]},"errors":[{"message":"list failure","path":["_entities"]}]}
                """)));

        // act
        var result = await ExecuteAsync(gateway, [2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3]
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":null}]}
            errors: [{"message":"list failure","path":["users",1,"total"]}]
            lookups: B:2 B:3

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheListErrorOnceWithoutAPath_When_AListErrorHasSeveralNullEntities()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            mockLastHop: _ => Task.FromResult(CreateJsonResponse(
                """{"data":{"_entities":[null,null]},"errors":[{"message":"list failure","path":["_entities"]}]}""")));

        // act
        var result = await ExecuteAsync(gateway, [2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3]
            data: {"users":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"list failure"}]
            lookups: B:2 B:3

            """);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private Task<Gateway> CreateGatewayAsync(
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockMiddleHop = null,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockLastHop = null)
    {
        var a = CreateSourceSchema(
            "a",
            builder => builder
                .AddApolloFederation()
                .AddQueryType<AUsers.Query>(d => d.Name("Query"))
                .AddType<AUsers.User>());
        var b = CreateSourceSchema(
            "b",
            builder => builder
                .AddApolloFederation()
                .AddQueryType<BSkus.Query>(d => d.Name("Query"))
                .AddType<BSkus.User>(),
            mockHttpResponse: mockMiddleHop);
        var c = CreateSourceSchema(
            "c",
            builder => builder
                .AddApolloFederation()
                .AddQueryType<CTotals.Query>(d => d.Name("Query"))
                .AddType<CTotals.User>(),
            mockHttpResponse: mockLastHop);

        return CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c)],
            enableGlobalObjectIdentification: false,
            includeOperationPlan: false);
    }

    private static async Task<string> ExecuteAsync(Gateway gateway, params int[][] idSets)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        foreach (var ids in idSets)
        {
            s_calls.Clear();
            var list = string.Join(", ", ids);
            using var response = await client.PostAsync(
                new OperationRequest($"{{ users(ids: [{list}]) {{ name total }} }}"),
                new Uri("http://localhost:5000/graphql"),
                TestContext.Current.CancellationToken);
            using var result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);

            text.AppendLine($"## ids [{list}]");
            text.AppendLine($"data: {result.Data.GetRawText()}");

            if (result.Errors.ValueKind is not JsonValueKind.Undefined)
            {
                text.AppendLine($"errors: {result.Errors.GetRawText()}");
            }

            text.AppendLine($"lookups: {string.Join(" ", s_calls.Order(StringComparer.Ordinal))}");
        }

        return text.ToString();
    }

    public static class AUsers
    {
        public sealed class Query
        {
            public IEnumerable<User> GetUsers(int[] ids) => ids.Select(i => new User { Id = i, Name = $"user-{i}" });
        }

        [Key("id")]
        public sealed class User
        {
            public int Id { get; init; }

            public string Name { get; init; } = default!;

            [ReferenceResolver]
            public static User ResolveReference(int id) => new() { Id = id, Name = $"user-{id}" };
        }
    }

    public static class BSkus
    {
        public sealed class Query
        {
            public string GetBVersion() => "1";
        }

        [Key("id")]
        public sealed class User
        {
            public int Id { get; init; }

            public string? Sku { get; init; }

            [ReferenceResolver]
            public static User? ResolveReference(int id)
            {
                s_calls.Enqueue("B:" + id);

                return id switch
                {
                    98 => throw new GraphQLException("B hop failed for 98"),
                    95 => throw new GraphQLException("B hop failed for 95"),
                    97 => null,
                    _ => new User { Id = id, Sku = $"sku-{id}" }
                };
            }
        }
    }

    public static class CTotals
    {
        public sealed class Query
        {
            public string GetCVersion() => "1";
        }

        [Key("sku")]
        public sealed class User
        {
            public string Sku { get; init; } = default!;

            public int? Total { get; init; }

            [ReferenceResolver]
            public static User? ResolveReference(string sku)
            {
                s_calls.Enqueue("C:" + sku);

                return sku == "sku-96"
                    ? throw new GraphQLException("C hop failed for sku-96")
                    : new User { Sku = sku, Total = int.Parse(sku[4..]) };
            }
        }
    }
}
