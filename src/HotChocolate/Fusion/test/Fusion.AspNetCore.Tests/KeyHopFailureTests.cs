using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public class KeyHopFailureTests : FusionTestBase
{
    private static readonly ConcurrentQueue<string> s_calls = new();

    [Fact]
    public async Task Execute_Should_NullOnlyTheFailedItem_When_MiddleHopThrowsForNestedKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, false, true, [2, 98, 3], [2, 3, 98], [98, 2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 98, 3] defer
            data: {"users":[{"name":"user-2"},{"name":"user-98"},{"name":"user-3"}]}
            incremental: [0]{"total":2} [1]{"total":null} [2]{"total":3}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-98","total":null}]}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98] defer
            data: {"users":[{"name":"user-2"},{"name":"user-3"},{"name":"user-98"}]}
            incremental: [0]{"total":2} [1]{"total":3} [2]{"total":null}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [98, 2, 3] plain
            data: {"users":[{"name":"user-98","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [98, 2, 3] defer
            data: {"users":[{"name":"user-98"},{"name":"user-2"},{"name":"user-3"}]}
            incremental: [0]{"total":null} [1]{"total":2} [2]{"total":3}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheFailedItem_When_MiddleHopThrowsForCompositeKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(CompositeKeyType);

        // act
        var result = await ExecuteAsync(gateway, false, true, [2, 98, 3], [98, 2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 98, 3] defer
            data: {"users":[{"name":"user-2"},{"name":"user-98"},{"name":"user-3"}]}
            incremental: [0]{"total":2} [1]{"total":null} [2]{"total":3}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [98, 2, 3] plain
            data: {"users":[{"name":"user-98","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [98, 2, 3] defer
            data: {"users":[{"name":"user-98"},{"name":"user-2"},{"name":"user-3"}]}
            incremental: [0]{"total":null} [1]{"total":2} [2]{"total":3}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheItemWithoutKey_When_MiddleHopReturnsNullForNestedKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, true, true, [2, 97, 3], [97, 2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-97","total":null},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3
            ## ids [2, 97, 3] defer
            data: {"users":[{"name":"user-2"},{"name":"user-97"},{"name":"user-3"}]}
            incremental: [0]{"total":2} [1]{"total":null} [2]{"total":3}
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3
            ## ids [97, 2, 3] plain
            data: {"users":[{"name":"user-97","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3
            ## ids [97, 2, 3] defer
            data: {"users":[{"name":"user-97"},{"name":"user-2"},{"name":"user-3"}]}
            incremental: [0]{"total":null} [1]{"total":2} [2]{"total":3}
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheItemWithoutKey_When_MiddleHopReturnsNullForCompositeKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(CompositeKeyType);

        // act
        var result = await ExecuteAsync(gateway, true, true, [2, 97, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-97","total":null},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:97 C:sku-2/eu C:sku-3/eu
            ## ids [2, 97, 3] defer
            data: {"users":[{"name":"user-2"},{"name":"user-97"},{"name":"user-3"}]}
            incremental: [0]{"total":2} [1]{"total":null} [2]{"total":3}
            lookups: B:2 B:3 B:97 C:sku-2/eu C:sku-3/eu

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheFailedItem_When_LastHopThrowsForNestedKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 96, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 96, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-96","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"C hop failed for sku-96","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:96 C:sku-2 C:sku-3 C:sku-96

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneErrorPerAffectedPath_When_LastHopFailsWithServerError()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            NestedKeyType,
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 98, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":null},{"name":"user-98","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"Unexpected Execution Error","path":["users",2,"total"]}]
            lookups: B:2 B:3 B:98

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneErrorPerAffectedPath_When_LastHopFailsWithTransportError()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            NestedKeyType,
            _ => throw new HttpRequestException("transport failure"));

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3] plain
            data: {"users":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"Unexpected Execution Error","path":["users",1,"total"]}]
            lookups: B:2 B:3

            """);
    }

    private Task<Gateway> CreateGatewayAsync(
        Type lastHopType,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockLastHop = null)
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>());
        var c = CreateSourceSchema(
            "c",
            builder => builder.AddQueryType(lastHopType),
            mockHttpResponse: mockLastHop);

        return CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c)],
            includeOperationPlan: false);
    }

    private static Type NestedKeyType => typeof(CNested.Query);

    private static Type CompositeKeyType => typeof(CComposite.Query);

    private static async Task<string> ExecuteAsync(
        Gateway gateway,
        bool includeErrors,
        bool includeDefer,
        params int[][] idSets)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        foreach (var ids in idSets)
        {
            var list = string.Join(", ", ids);

            foreach (var defer in new[] { false, true })
            {
                if (defer && !includeDefer)
                {
                    continue;
                }

                s_calls.Clear();
                var selection = defer ? "name ... @defer { total }" : "name total";
                var request = new OperationRequest($"{{ users(ids: [{list}]) {{ {selection} }} }}");

                text.AppendLine($"## ids [{list}] {(defer ? "defer" : "plain")}");

                using var response = await client.PostAsync(
                    request,
                    new Uri("http://localhost:5000/graphql"),
                    TestContext.Current.CancellationToken);

                await foreach (var result in response.ReadAsResultStreamAsync())
                {
                    AppendResult(text, result, includeErrors);
                }

                text.AppendLine($"lookups: {string.Join(" ", s_calls.Order(StringComparer.Ordinal))}");
            }
        }

        return text.ToString();
    }

    private static void AppendResult(StringBuilder text, OperationResult result, bool includeErrors)
    {
        if (result.Data.ValueKind is not JsonValueKind.Undefined)
        {
            text.AppendLine($"data: {result.Data.GetRawText()}");
        }

        if (result.Incremental.ValueKind is JsonValueKind.Array)
        {
            text.Append("incremental:");

            foreach (var entry in result.Incremental.EnumerateArray())
            {
                var subPath = entry.GetProperty("subPath").GetRawText();
                text.Append($" {subPath}{entry.GetProperty("data").GetRawText()}");
            }

            text.AppendLine();
        }

        if (includeErrors && result.Errors.ValueKind is not JsonValueKind.Undefined)
        {
            text.AppendLine($"errors: {result.Errors.GetRawText()}");
        }
    }

    public static class A
    {
        [EntityKey("id")]
        public record User(int Id, string Name);

        public class Query
        {
            public IEnumerable<User> GetUsers(int[] ids) => ids.Select(i => new User(i, $"user-{i}"));
        }
    }

    public static class B
    {
        public record Info([property: Shareable] string Sku);

        public record User(int Id, [property: Shareable] Info Info, [property: Shareable] string Region);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id)
            {
                s_calls.Enqueue("B:" + id);

                return id switch
                {
                    98 => throw new GraphQLException("B hop failed for 98"),
                    97 => null,
                    _ => new User(id, new Info($"sku-{id}"), "eu")
                };
            }
        }
    }

    public static class CNested
    {
        public record Info([property: Shareable] string Sku);

        public record User([property: Shareable] Info Info, int? Total);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserBySku([Is("info.sku")] string sku)
            {
                s_calls.Enqueue("C:" + sku);

                if (sku == "sku-96")
                {
                    throw new GraphQLException("C hop failed for sku-96");
                }

                return new User(new Info(sku), int.Parse(sku[4..]));
            }
        }
    }

    public static class CComposite
    {
        public record Info([property: Shareable] string Sku);

        public record User([property: Shareable] Info Info, [property: Shareable] string Region, int? Total);

        public record KeyInput(string Sku, string Region);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserByKey([Is("{ sku: info.sku, region }")] KeyInput key)
            {
                s_calls.Enqueue("C:" + key.Sku + "/" + key.Region);
                return new User(new Info(key.Sku), key.Region, int.Parse(key.Sku[4..]));
            }
        }
    }
}
