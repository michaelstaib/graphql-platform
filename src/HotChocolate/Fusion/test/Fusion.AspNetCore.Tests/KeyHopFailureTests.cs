using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Execution.Configuration;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Resolvers;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

[Collection("KeyHopFailure")]
public class KeyHopFailureTests : FusionTestBase
{
    private static readonly ConcurrentQueue<string> s_calls = new();
    private static int s_delayB;
    private static int s_delayC;
    private static int s_delayD;
    private static int s_delayE;
    private static int s_delayH;

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
            incremental errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-98","total":null}]}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98] defer
            data: {"users":[{"name":"user-2"},{"name":"user-3"},{"name":"user-98"}]}
            incremental: [0]{"total":2} [1]{"total":3} [2]{"total":null}
            incremental errors: [{"message":"B hop failed for 98","path":["users",2,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [98, 2, 3] plain
            data: {"users":[{"name":"user-98","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [98, 2, 3] defer
            data: {"users":[{"name":"user-98"},{"name":"user-2"},{"name":"user-3"}]}
            incremental: [0]{"total":null} [1]{"total":2} [2]{"total":3}
            incremental errors: [{"message":"B hop failed for 98","path":["users",0,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheFailedItem_When_MiddleHopThrowsForCompositeKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(CompositeKeyType);

        // act
        var result = await ExecuteAsync(gateway, false, true, [2, 98, 3], [2, 3, 98], [98, 2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 98, 3] defer
            data: {"users":[{"name":"user-2"},{"name":"user-98"},{"name":"user-3"}]}
            incremental: [0]{"total":2} [1]{"total":null} [2]{"total":3}
            incremental errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 98] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-98","total":null}]}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 98] defer
            data: {"users":[{"name":"user-2"},{"name":"user-3"},{"name":"user-98"}]}
            incremental: [0]{"total":2} [1]{"total":3} [2]{"total":null}
            incremental errors: [{"message":"B hop failed for 98","path":["users",2,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [98, 2, 3] plain
            data: {"users":[{"name":"user-98","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [98, 2, 3] defer
            data: {"users":[{"name":"user-98"},{"name":"user-2"},{"name":"user-3"}]}
            incremental: [0]{"total":null} [1]{"total":2} [2]{"total":3}
            incremental errors: [{"message":"B hop failed for 98","path":["users",0,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu

            """);
    }

    [Fact]
    public async Task Execute_Should_NullOnlyTheItemWithoutKey_When_MiddleHopReturnsNullForNestedKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, false, true, [2, 97, 3], [2, 3, 97], [97, 2, 3]);

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
            ## ids [2, 3, 97] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-97","total":null}]}
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3
            ## ids [2, 3, 97] defer
            data: {"users":[{"name":"user-2"},{"name":"user-3"},{"name":"user-97"}]}
            incremental: [0]{"total":2} [1]{"total":3} [2]{"total":null}
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
        var result = await ExecuteAsync(gateway, false, true, [2, 97, 3], [2, 3, 97], [97, 2, 3]);

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
            ## ids [2, 3, 97] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-97","total":null}]}
            lookups: B:2 B:3 B:97 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 97] defer
            data: {"users":[{"name":"user-2"},{"name":"user-3"},{"name":"user-97"}]}
            incremental: [0]{"total":2} [1]{"total":3} [2]{"total":null}
            lookups: B:2 B:3 B:97 C:sku-2/eu C:sku-3/eu
            ## ids [97, 2, 3] plain
            data: {"users":[{"name":"user-97","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            lookups: B:2 B:3 B:97 C:sku-2/eu C:sku-3/eu
            ## ids [97, 2, 3] defer
            data: {"users":[{"name":"user-97"},{"name":"user-2"},{"name":"user-3"}]}
            incremental: [0]{"total":null} [1]{"total":2} [2]{"total":3}
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
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"B hop failed for 98","path":["users",1,"total"]},{"message":"Unexpected Execution Error","path":["users",2,"total"]}]
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

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceAtTheDependentField_When_MiddleHopThrowsForNestedKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 98, 3], [98, 2, 3], [2, 3, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [98, 2, 3] plain
            data: {"users":[{"name":"user-98","total":null},{"name":"user-2","total":2},{"name":"user-3","total":3}]}
            errors: [{"message":"B hop failed for 98","path":["users",0,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-98","total":null}]}
            errors: [{"message":"B hop failed for 98","path":["users",2,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceAtTheDependentField_When_MiddleHopThrowsForCompositeKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(CompositeKeyType);

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 98, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheDependentField_When_KeyFieldFailsWithPartialData()
    {
        // arrange
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<BFlat.Query>());
        var c = CreateSourceSchema("c", b => b.AddQueryType<CFlat.Query>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c)],
            includeOperationPlan: false);

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 98, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"sku failed for 98","path":["users",1,"total"]}]
            lookups: C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneErrorPerAffectedPath_When_MiddleHopFailsWithServerError()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            NestedKeyType,
            mockMiddleHop: _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3] plain
            data: {"users":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"Unexpected Execution Error","path":["users",1,"total"]}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneErrorPerAffectedPath_When_MiddleHopFailsWithTransportError()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(
            NestedKeyType,
            mockMiddleHop: _ => throw new HttpRequestException("transport failure"));

        // act
        var result = await ExecuteAsync(gateway, true, false, [2, 98, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":null},{"name":"user-98","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"total"]},{"message":"Unexpected Execution Error","path":["users",1,"total"]},{"message":"Unexpected Execution Error","path":["users",2,"total"]}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_NullTheNullableItem_When_NonNullDependentFieldHasNoKeyFromNestedKeyHop()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, "{ nullableUsers(ids: [$ids]) { name totalNN } }", [2, 97, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 3]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},null,{"name":"user-3","totalNN":300}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_PropagateNullFurther_When_NonNullDependentFieldHasNoKeyAndItemsAreNonNull()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, "{ users(ids: [$ids]) { name totalNN } }", [2, 97, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 3]
            data: null
            errors: [{"message":"Cannot return null for non-nullable field.","path":["users",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NullTheNullableItem_When_NonNullDependentFieldHasNoKeyFromFlatKeyHop()
    {
        // arrange
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<BFlat.Query>());
        var c = CreateSourceSchema("c", b => b.AddQueryType<CFlat.Query>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c)],
            includeOperationPlan: false);

        // act
        var result = await ExecuteAsync(gateway, "{ nullableUsers(ids: [$ids]) { name totalNN } }", [2, 94, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 94, 3]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},null,{"name":"user-3","totalNN":300}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheDependentField_When_DeferredMiddleHopThrows()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(gateway, true, true, [2, 98, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98, 3] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-98","total":null},{"name":"user-3","total":3}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 98, 3] defer
            data: {"users":[{"name":"user-2"},{"name":"user-98"},{"name":"user-3"}]}
            incremental: [0]{"total":2} [1]{"total":null} [2]{"total":3}
            incremental errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportTheErrorOnAFieldThatDependsOnAnotherHop_When_ASiblingKeyHopThrows()
    {
        // arrange
        using var gateway = await CreateFiveSchemaGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, "{ users(ids: [$ids]) { total score } }", [2, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98]
            data: {"users":[{"total":2,"score":6},{"total":null,"score":null}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:98 C:sku-2

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportTheErrorOnADescendantField_When_TheParentKeyHopThrows()
    {
        // arrange
        using var gateway = await CreateFiveSchemaGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, "{ users(ids: [$ids]) { total friend { score } } }", [2, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98]
            data: {"users":[{"total":2,"friend":{"score":9}},{"total":null,"friend":{"score":null}}]}
            errors: [{"message":"B hop failed for 98","path":["users",1,"total"]}]
            lookups: B:2 B:98 C:sku-2

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameErrors_When_TheSiblingHopsFinishInEitherOrder()
    {
        // arrange
        using var gateway = await CreateFiveSchemaGatewayAsync();
        const string query = "{ nullableUsers(ids: [$ids]) { total scoreNN } }";

        // act
        s_delayB = 0;
        s_delayE = 300;
        var errorHopFirst = await ExecuteAsync(gateway, query, [2, 98]);

        s_delayB = 300;
        s_delayE = 0;
        var violationHopFirst = await ExecuteAsync(gateway, query, [2, 98]);
        s_delayB = 0;
        s_delayE = 0;

        // assert
        Assert.Equal(errorHopFirst, violationHopFirst);
        errorHopFirst.MatchInlineSnapshot(
            """
            ## ids [2, 98]
            data: {"nullableUsers":[{"total":2,"scoreNN":6},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"scoreNN"],"extensions":{"code":"HC0018"}},{"message":"B hop failed for 98","path":["nullableUsers",1,"total"]}]
            lookups: B:2 B:98 C:sku-2

            """);
    }

    private async Task<Gateway> CreateFiveSchemaGatewayAsync()
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>());
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var d = CreateSourceSchema("d", b => b.AddQueryType<D.Query>());
        var e = CreateSourceSchema("e", b => b.AddQueryType<E.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("d", d), ("e", e)],
            includeOperationPlan: false);
    }

    [Fact]
    public async Task Execute_Should_NullTheNullableItem_When_NonNullFieldBehindThreeSkippedHops()
    {
        // arrange
        using var gateway = await CreateThreeHopGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, "{ nullableUsers(ids: [$ids]) { name rankNN } }", [98], [2, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [98]
            data: {"nullableUsers":[null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"rankNN"],"extensions":{"code":"HC0018"}}]
            lookups: 
            ## ids [2, 98]
            data: {"nullableUsers":[{"name":"user-2","rankNN":18},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"rankNN"],"extensions":{"code":"HC0018"}}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_PropagateNullFurther_When_NonNullFieldBehindThreeSkippedHopsAndItemsAreNonNull()
    {
        // arrange
        using var gateway = await CreateThreeHopGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, "{ users(ids: [$ids]) { name rankNN } }", [98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [98]
            data: null
            errors: [{"message":"Cannot return null for non-nullable field.","path":["users",0,"rankNN"],"extensions":{"code":"HC0018"}}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_NullTheNullableRoot_When_NonNullFieldDependsOnANullRootKey()
    {
        // arrange
        using var gateway = await CreateThreeHopGatewayAsync();

        // act
        var result = await ExecuteAsync(gateway, "{ user(id: $ids) { code scoreNN } }", [98], [2]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [98]
            data: {"user":null}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["user","scoreNN"],"extensions":{"code":"HC0018"}}]
            lookups: 
            ## ids [2]
            data: {"user":{"code":"code-2","scoreNN":6}}
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfEveryItem_When_MiddleHopFailsForNullableListOfNonNullItems()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType, mockMiddleHop: ServerError);

        // act
        var result = await ExecuteAsync(gateway, "{ userList(ids: [$ids]) { totalNN } }", [2, 3, 4, 5]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3, 4, 5]
            data: {"userList":null}
            errors: [{"message":"Unexpected Execution Error","path":["userList",0,"totalNN"]},{"message":"Unexpected Execution Error","path":["userList",1,"totalNN"]},{"message":"Unexpected Execution Error","path":["userList",2,"totalNN"]},{"message":"Unexpected Execution Error","path":["userList",3,"totalNN"]}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfEveryItem_When_LastHopFailsForNullableListOfNonNullItems()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType, mockLastHop: ServerError);

        // act
        var result = await ExecuteAsync(gateway, "{ userList(ids: [$ids]) { totalNN } }", [2, 3, 4, 5]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 3, 4, 5]
            data: {"userList":null}
            errors: [{"message":"Unexpected Execution Error","path":["userList",0,"totalNN"]},{"message":"Unexpected Execution Error","path":["userList",1,"totalNN"]},{"message":"Unexpected Execution Error","path":["userList",2,"totalNN"]},{"message":"Unexpected Execution Error","path":["userList",3,"totalNN"]}]
            lookups: B:2 B:3 B:4 B:5

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportErrorsUnderAnotherField_When_ViolationNullsTheRoot()
    {
        // arrange
        using var gateway = await CreateFiveSchemaGatewayAsync(ServerErrorAfterDelayB);
        const string query = "{ users(ids: [2, 3]) { totalNN } other: users(ids: [5, 6]) { score } }";

        // act
        var results = await ExecuteInEveryOrderAsync(gateway, query);

        // assert
        Assert.Single(results.Distinct());
        results[0].MatchInlineSnapshot(
            """
            data: null
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"totalNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_KeepTheOtherFieldUntouched_When_ViolationNullsTheItems()
    {
        // arrange
        using var gateway = await CreateFiveSchemaGatewayAsync(ServerErrorAfterDelayB);
        const string query = "{ nullableUsers(ids: [2, 3]) { totalNN } other: users(ids: [5, 6]) { score } }";
        const string plainQuery = "{ other: users(ids: [5, 6]) { score } }";

        // act
        var results = await ExecuteInEveryOrderAsync(gateway, query);
        var plain = await ExecuteQueryAsync(gateway, plainQuery);

        // assert
        Assert.Single(results.Distinct());
        results[0].MatchInlineSnapshot(
            """
            data: {"nullableUsers":[null,null],"other":[{"score":6},{"score":6}]}
            errors: [{"message":"Unexpected Execution Error","path":["nullableUsers",0,"totalNN"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"totalNN"]}]

            """);
        plain.MatchInlineSnapshot(
            """
            data: {"other":[{"score":6},{"score":6}]}

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOneViolation_When_TwoHopsMissTheSameTarget()
    {
        // arrange
        using var gateway = await CreateTwoHopsOnTheSameTargetGatewayAsync();
        const string query = "{ nullableUsers(ids: [2, 97, 3]) { totalNN bonusNN } }";

        // act
        var results = await ExecuteInEveryOrderAsync(gateway, query);

        // assert
        Assert.Single(results.Distinct());
        results[0].MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"totalNN":200,"bonusNN":20},null,{"totalNN":300,"bonusNN":30}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolationForAnItem_When_ItemWasNulledByARaisedError()
    {
        // arrange
        using var gateway = await CreateRaisedErrorGatewayAsync();

        // act
        var result = await ExecuteQueryAsync(
            gateway,
            "{ nullableUsers(ids: [2, 93, 3]) { badNN totalNN } }");

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"badNN":2,"totalNN":200},null,{"badNN":3,"totalNN":300}]}
            errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolationForItems_When_ListWasNulledByARaisedError()
    {
        // arrange
        using var gateway = await CreateRaisedErrorGatewayAsync();

        // act
        var result = await ExecuteQueryAsync(
            gateway,
            "{ userList(ids: [93, 97, 2]) { badNN totalNN } }");

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"userList":null}
            errors: [{"message":"I hop failed for 93","path":["userList",0,"badNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotWriteViolations_When_ARaisedErrorNulledTheRootBeforeThePass()
    {
        // arrange
        using var gateway = await CreateRaisedErrorGatewayAsync();

        // act
        var result = await ExecuteQueryAsync(
            gateway,
            "{ users(ids: [93, 97, 3]) { badNN totalNN } }");

        // assert
        result.MatchInlineSnapshot(
            """
            data: null
            errors: [{"message":"I hop failed for 93","path":["users",0,"badNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotNullTheRoot_When_SecondViolationMeetsANulledList()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ userList(ids: [$ids]) { totalNN } }",
            [2, 97, 97, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 97, 3]
            data: {"userList":null}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["userList",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolation_When_DependentFieldIsSkippedOrNotIncluded()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var skipped = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { name totalNN @skip(if: true) } }",
            [2, 97]);
        var notIncluded = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { name totalNN @include(if: false) } }",
            [2, 97]);

        // assert
        skipped.MatchInlineSnapshot(
            """
            ## ids [2, 97]
            data: {"nullableUsers":[{"name":"user-2"},{"name":"user-97"}]}
            lookups: 

            """);
        notIncluded.MatchInlineSnapshot(
            """
            ## ids [2, 97]
            data: {"nullableUsers":[{"name":"user-2"},{"name":"user-97"}]}
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolation_When_FieldIsExcludedByAVariable()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var include = await ExecuteQueryAsync(
            gateway,
            "query($s: Boolean!) { nullableUsers(ids: [2, 97]) { name total totalNN @include(if: $s) } }",
            false);
        var skip = await ExecuteQueryAsync(
            gateway,
            "query($s: Boolean!) { nullableUsers(ids: [2, 97]) { name total totalNN @skip(if: $s) } }",
            true);

        // assert
        include.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"name":"user-2","total":2},{"name":"user-97","total":null}]}

            """);
        skip.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"name":"user-2","total":2},{"name":"user-97","total":null}]}

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolation_When_FragmentIsExcludedByAVariable()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var include = await ExecuteQueryAsync(
            gateway,
            "query($s: Boolean!) { nullableUsers(ids: [2, 97]) { name ... @include(if: $s) { totalNN } } }",
            false);
        var skip = await ExecuteQueryAsync(
            gateway,
            "query($s: Boolean!) { nullableUsers(ids: [2, 97]) { name ... @skip(if: $s) { totalNN } } }",
            true);

        // assert
        include.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"name":"user-2"},{"name":"user-97"}]}

            """);
        skip.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"name":"user-2"},{"name":"user-97"}]}

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolation_When_ExcludedFieldDependsOnAFailedHop()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType, mockMiddleHop: ServerError);

        // act
        var result = await ExecuteQueryAsync(
            gateway,
            "query($s: Boolean!) { nullableUsers(ids: [2, 3]) { name total totalNN @include(if: $s) } }",
            false);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["nullableUsers",0,"total"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"total"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotRunTheNextMutationRoot_When_ViolationOfTheFirstRootNullsTheData()
    {
        // arrange
        using var gateway = await CreateOneHopMutationGatewayAsync();

        // act
        var result = await ExecuteMutationAsync(
            gateway,
            "mutation { first: createUserNN(id: 94) { name nickRankNN } second: touch }");

        // assert
        result.MatchInlineSnapshot(
            """
            data: null
            errors: [{"message":"Cannot return null for non-nullable field.","path":["first","nickRankNN"],"extensions":{"code":"HC0018"}}]
            calls: A:create-94

            """);
    }

    [Fact]
    public async Task Execute_Should_RunTheNextMutationRootOnce_When_ViolationOfTheFirstRootNullsANullableField()
    {
        // arrange
        using var gateway = await CreateOneHopMutationGatewayAsync();

        // act
        var result = await ExecuteMutationAsync(
            gateway,
            "mutation { first: createUser(id: 94) { name nickRankNN } second: touch }");

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"first":null,"second":1}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["first","nickRankNN"],"extensions":{"code":"HC0018"}}]
            calls: A:create-94 A:touch

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameViolations_When_RequestGroupingBatchesTheLookupsOfNullableItems()
    {
        // arrange
        using var grouped = await CreateBatchedGatewayAsync(enableRequestGrouping: true);
        using var ungrouped = await CreateBatchedGatewayAsync(enableRequestGrouping: false);
        const string query = "{ nullableUsers(ids: [2, 3]) { name kayNN } accounts(ids: [5]) { kayNN } }";

        // act
        var groupedResult = await ExecuteQueryAsync(grouped, query);
        var ungroupedResult = await ExecuteQueryAsync(ungrouped, query);

        // assert
        Assert.Equal(ungroupedResult, groupedResult);
        groupedResult.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[null,null],"accounts":[{"kayNN":3}]}
            errors: [{"message":"Unexpected Execution Error","path":["nullableUsers",0,"kayNN"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"kayNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameViolations_When_RequestGroupingBatchesTheLookupsOfNonNullItems()
    {
        // arrange
        using var grouped = await CreateBatchedGatewayAsync(enableRequestGrouping: true);
        using var ungrouped = await CreateBatchedGatewayAsync(enableRequestGrouping: false);
        const string query = "{ users(ids: [2, 3]) { name kayNN } accounts(ids: [5]) { kayNN } }";

        // act
        var groupedResult = await ExecuteQueryAsync(grouped, query);
        var ungroupedResult = await ExecuteQueryAsync(ungrouped, query);

        // assert
        Assert.Equal(ungroupedResult, groupedResult);
        groupedResult.MatchInlineSnapshot(
            """
            data: null
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"kayNN"]}]

            """);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Execute_Should_ReportViolationOfTheFailedDefinition_When_SkipsOfTheBatchArriveInAnyOrder(
        bool enableRequestGrouping,
        bool failureArrivesFirst)
    {
        // arrange
        using var gateway = await CreateSplitBatchedGatewayAsync(enableRequestGrouping);
        ArrivalGate.Current = new ArrivalGate(failureArrivesFirst);

        // act
        string result;

        try
        {
            result = await ExecuteQueryAsync(
                gateway,
                "query($s: Boolean!) { nullableUsers(ids: [2, 3]) { name kayNN } "
                + "accounts(ids: [5]) { id code @include(if: $s) kayNN @include(if: $s) } }",
                false);
        }
        finally
        {
            ArrivalGate.Current = null;
        }

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[null,null],"accounts":[{"id":5}]}
            errors: [{"message":"Unexpected Execution Error","path":["nullableUsers",0,"kayNN"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"kayNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportViolation_When_BatchedDefinitionIsExcludedByAVariable()
    {
        // arrange
        using var grouped = await CreateBatchedGatewayAsync(enableRequestGrouping: true);

        // act
        var result = await ExecuteQueryAsync(
            grouped,
            "query($s: Boolean!) { nullableUsers(ids: [2, 3]) { name kayNN @include(if: $s) } accounts(ids: [5]) { kayNN } }",
            false);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"name":"user-2"},{"name":"user-3"}],"accounts":[{"kayNN":3}]}

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportViolationOnlyForTheItemWithoutKey_When_SingleHopMissesTheKey()
    {
        // arrange
        using var gateway = await CreateOneHopGatewayAsync();

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { name nickRankNN } }",
            [94, 2, 3],
            [2, 94, 3],
            [2, 3, 94]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [94, 2, 3]
            data: {"nullableUsers":[null,{"name":"user-2","nickRankNN":14},{"name":"user-3","nickRankNN":21}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"nickRankNN"],"extensions":{"code":"HC0018"}}]
            lookups: 
            ## ids [2, 94, 3]
            data: {"nullableUsers":[{"name":"user-2","nickRankNN":14},null,{"name":"user-3","nickRankNN":21}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"nickRankNN"],"extensions":{"code":"HC0018"}}]
            lookups: 
            ## ids [2, 3, 94]
            data: {"nullableUsers":[{"name":"user-2","nickRankNN":14},{"name":"user-3","nickRankNN":21},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",2,"nickRankNN"],"extensions":{"code":"HC0018"}}]
            lookups: 

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportViolationOnlyForTheFailedItem_When_MiddleHopFailsForNestedKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { name totalNN } }",
            [98, 2, 3],
            [2, 98, 3],
            [2, 3, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [98, 2, 3]
            data: {"nullableUsers":[null,{"name":"user-2","totalNN":200},{"name":"user-3","totalNN":300}]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",0,"totalNN"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 98, 3]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},null,{"name":"user-3","totalNN":300}]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",1,"totalNN"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},{"name":"user-3","totalNN":300},null]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",2,"totalNN"]}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportViolationOnlyForTheFailedItem_When_MiddleHopFailsForCompositeKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(CompositeKeyType);

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { name totalNN } }",
            [98, 2, 3],
            [2, 98, 3],
            [2, 3, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [98, 2, 3]
            data: {"nullableUsers":[null,{"name":"user-2","totalNN":200},{"name":"user-3","totalNN":300}]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",0,"totalNN"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 98, 3]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},null,{"name":"user-3","totalNN":300}]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",1,"totalNN"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 98]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},{"name":"user-3","totalNN":300},null]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",2,"totalNN"]}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportViolationOnlyForTheFailedItem_When_FirstHopOfThreeFails()
    {
        // arrange
        using var gateway = await CreateThreeHopGatewayAsync();
        const string query = "{ nullableUsers(ids: [98, 2, 98, 3]) { name rankNN } }";

        // act
        var results = await ExecuteInEveryOrderAsync(gateway, query);

        // assert
        Assert.Single(results.Distinct());
        results[0].MatchInlineSnapshot(
            """
            data: {"nullableUsers":[null,{"name":"user-2","rankNN":18},null,{"name":"user-3","rankNN":18}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"rankNN"],"extensions":{"code":"HC0018"}},{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",2,"rankNN"],"extensions":{"code":"HC0018"}}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportViolationForTheDeferredField_When_DeferredHopMissesTheKey()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { name ... @defer { totalNN } } }",
            [2, 97, 3]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 97, 3]
            data: {"nullableUsers":[{"name":"user-2"},{"name":"user-97"},{"name":"user-3"}]}
            incremental: [0]{"totalNN":200} [2]{"totalNN":300}
            incremental errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:97 C:sku-2 C:sku-3

            """);
    }

    [Fact]
    public async Task Execute_Should_CompleteTheDeferredGroupWithTheViolation_When_DeferredHopMissesKeyAndItemsAreNonNull()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteDeferredPayloadsAsync(
            gateway,
            "{ users(ids: [2, 97, 3]) { name ... @defer { totalNN } } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {"data":{"users":[{"name":"user-2"},{"name":"user-97"},{"name":"user-3"}]},"pending":[{"id":"0","path":["users"]}],"hasNext":true}
            {"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["users",1,"totalNN"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameErrors_When_HopsFinishInAnyOrderAndAFieldErrorMeetsAViolation()
    {
        // arrange
        const string query = "{ nullableUsers(ids: [2, 98]) { total scoreNN } }";
        string[] hops = ["B", "C", "D", "E"];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreateSequencedGatewayAsync,
            query,
            include: null,
            hops,
            [("B", "C"), ("E", "D")],
            [],
            []);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"total":2,"scoreNN":6},null]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",1,"total"]},{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"scoreNN"],"extensions":{"code":"HC0018"}}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameErrors_When_HopsFinishInAnyOrderAndANulledTargetFailsInTheKeyChain()
    {
        // arrange
        const string query = "{ nullableUsers(ids: [2, 93]) { code score totalNN } }";
        string[] hops = ["B", "C", "D", "E"];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreateSequencedGatewayAsync,
            query,
            include: null,
            hops,
            [("B", "C"), ("E", "D")],
            [],
            []);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"code":"code-2","score":6,"totalNN":200},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}},{"message":"E hop failed for 93","path":["nullableUsers",1,"code"]},{"message":"E hop failed for 93","path":["nullableUsers",1,"score"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameErrors_When_HopsFinishInAnyOrderAndTheErrorMustReachTheThirdHop()
    {
        // arrange
        const string query = "{ nullableUsers(ids: [2, 93]) { rank totalNN } }";
        string[] hops = ["B", "C", "D", "E", "F"];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreateSequencedGatewayAsync,
            query,
            include: null,
            hops,
            [("B", "C"), ("E", "D"), ("D", "F")],
            [],
            []);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"rank":12,"totalNN":200},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}},{"message":"E hop failed for 93","path":["nullableUsers",1,"rank"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorsByHopDepthInEveryOrder_When_RequirementsOfTwoDepthsAreMissing()
    {
        // arrange
        var sequence = new HopSequence();
        var errors = new List<string>();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateHopSourcesSequencedGatewayAsync(
                sequence,
                enableRequestGrouping,
                [
                    ("b", b => b.AddQueryType<B.Query>(), "B", false),
                    ("q", q => q.AddQueryType<QCode.Query>(), "Q", false),
                    ("r", r => r.AddQueryType<RScore.Query>(), "R", false),
                    ("z", z => z.AddQueryType<ZTotal.Query>(), null, false)
                ]);

            foreach (var list in new[] { "userList", "nullableUsers" })
            {
                foreach (var order in GetHopOrders(["B", "Q", "R"], [("Q", "R")]))
                {
                    sequence.Start(order);
                    errors.Add(
                        await ExecuteForErrorMessagesAtPathAsync(
                            gateway,
                            $"{{ {list}(ids: [98, 2]) {{ total }} }}",
                            $$"""["{{list}}",0,"total"]"""));
                }
            }
        }

        sequence.Start([]);

        // assert
        Assert.Equal(12, errors.Count);
        Assert.Single(errors.Distinct()).MatchInlineSnapshot(
            """
            B hop failed for 98
            R failed for 98

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheSameErrorsInEveryOrder_When_DependentsTwoLevelsDeepAreBelowANulledItem()
    {
        // arrange
        const string query = "{ nullableUsers(ids: [2, 93]) { rank score code totalNN } }";
        var sequence = new HopSequence();
        var errors = new List<string>();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateSequencedGatewayAsync(sequence, enableRequestGrouping);

            foreach (var order in GetHopOrders(["B", "C", "D", "E", "F"], [("B", "C"), ("E", "D"), ("D", "F")]))
            {
                sequence.Start(order);
                errors.Add(await ExecuteForErrorsInResponseOrderAsync(gateway, query));
            }
        }

        sequence.Start([]);

        // assert
        Assert.Single(errors.Distinct()).MatchInlineSnapshot(
            """
            E hop failed for 93 at ["nullableUsers",1,"code"]
            E hop failed for 93 at ["nullableUsers",1,"rank"]
            E hop failed for 93 at ["nullableUsers",1,"score"]
            Cannot return null for non-nullable field. at ["nullableUsers",1,"totalNN"]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotReportTheErrorOnAnExcludedField_When_TheHopOfItFailsForTheWholeRequest()
    {
        // arrange
        using var gateway = await CreateFiveSchemaGatewayAsync(mockCodeHop: ServerError);
        const string query = "query($s: Boolean!) { nullableUsers(ids: [2, 3]) { code @include(if: $s) score } }";

        // act
        var excluded = await ExecuteQueryAsync(gateway, query, false);
        var included = await ExecuteQueryAsync(gateway, query, true);

        // assert
        excluded.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"score":null},{"score":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["nullableUsers",0,"score"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"score"]}]

            """);
        included.MatchInlineSnapshot(
            """
            data: {"nullableUsers":[{"score":null,"code":null},{"score":null,"code":null}]}
            errors: [{"message":"Unexpected Execution Error","path":["nullableUsers",0,"code"]},{"message":"Unexpected Execution Error","path":["nullableUsers",0,"score"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"code"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"score"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_NotRepeatTheErrorOnTheDependentField_When_ANonNullPublicOriginFieldFails()
    {
        // arrange
        using var gateway = await CreateGatewayAsync(NestedKeyType);

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ nullableUsers(ids: [$ids]) { info { sku } total } }",
            [2, 98]);

        // assert
        result.MatchInlineSnapshot(
            """
            ## ids [2, 98]
            data: {"nullableUsers":[{"info":{"sku":"sku-2"},"total":2},null]}
            errors: [{"message":"B hop failed for 98","path":["nullableUsers",1,"info"]}]
            lookups: B:2 B:98 C:sku-2

            """);
    }

    [Fact]
    public async Task Execute_Should_CompleteTheRequest_When_ReportingTheFailureOfTheMiddleHopThrows()
    {
        // arrange
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: ServerError);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c)],
            configureGatewayBuilder: builder => builder.AddErrorFilter(error =>
                error.Path?.ToString().EndsWith("total", StringComparison.Ordinal) == true
                    ? throw new InvalidOperationException("reporting failed")
                    : error),
            includeOperationPlan: false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        // act
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.PostAsync(
            new OperationRequest("{ users(ids: [2, 3]) { name total } }"),
            new Uri("http://localhost:5000/graphql"),
            cts.Token);
        using var result = await response.ReadAsResultAsync(cts.Token);

        // assert
        result.Data.GetRawText().MatchInlineSnapshot(
            """
            {"users":[{"name":"user-2","total":null},{"name":"user-3","total":null}]}
            """);
    }

    [Fact]
    public async Task Execute_Should_ReportEveryRaisedErrorOnce_When_AccountsKeyHopFailsBeforeUsersKeyHop()
    {
        // arrange
        string[] order = ["accountById", "accountByCode", "userById", "userBySku"];

        // act
        var result = await ExecuteKeyHopRaceAsync(order);

        // assert
        result.MatchInlineSnapshot(
            """
            data: null
            errors: [{"message":"Unexpected Execution Error","path":["nullableAccounts",0,"kayNN"]},{"message":"Unexpected Execution Error","path":["users",0,"kayNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportOnlyTheUsersErrors_When_UsersKeyHopFailsBeforeAccountsKeyHop()
    {
        // arrange
        string[] order = ["userById", "userBySku", "accountById", "accountByCode"];

        // act
        var result = await ExecuteKeyHopRaceAsync(order);

        // assert
        result.MatchInlineSnapshot(
            """
            data: null
            errors: [{"message":"Unexpected Execution Error","path":["users",0,"kayNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheRaisedErrorOnce_When_ASiblingErrorNulledTheListInAnyOrder()
    {
        // arrange
        string[][] orders = [["B", "C", "I"], ["B", "I", "C"]];
        var sequence = new HopSequence();
        using var gateway = await CreateInFlightRaisedErrorSequencedGatewayAsync(sequence, enableRequestGrouping: true);
        var results = new List<string>();

        // act
        foreach (var order in orders)
        {
            sequence.Start(order);
            var result = await ExecuteWithSortedErrorsAsync(
                gateway,
                "{ userList(ids: [96, 93]) { total badNN } }",
                include: null);
            results.Add($"## {string.Join(" ", order)}\n{result}");
        }

        // assert
        results.MatchInlineSnapshots(
        [
            """
            ## B C I
            data: {"userList":null}
            errors: [{"message":"C hop failed for sku-96","path":["userList",0,"total"]},{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

            """,
            """
            ## B I C
            data: {"userList":null}
            errors: [{"message":"C hop failed for sku-96","path":["userList",0,"total"]},{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_ReportOnlyTheNullingError_When_TheListIsNullBeforeTheKeyHopsStart()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateMergeOrderedRaisedErrorSequencedGatewayAsync(sequence);
        sequence.Start(["I", "B", "C"]);

        // act
        var result = await ExecuteWithSortedErrorsAsync(
            gateway,
            "{ userList(ids: [96, 93]) { total badNN } }",
            include: null);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"userList":null}
            errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheRaisedError_When_TheKeyHopsAreMergedBeforeTheListIsNull()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateMergeOrderedRaisedErrorSequencedGatewayAsync(sequence);
        sequence.Start(["B", "C", "I"]);

        // act
        var result = await ExecuteWithSortedErrorsAsync(
            gateway,
            "{ userList(ids: [96, 93]) { total badNN } }",
            include: null);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"userList":null}
            errors: [{"message":"C hop failed for sku-96","path":["userList",0,"total"]},{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheRaisedErrorsOfAbstractTargets_When_ASiblingErrorNulledTheList()
    {
        // arrange
        string[] nulling =
        [
            """{"message":"Z2 hop failed for 93","path":["petList",1,"barkNN"]}""",
            """{"message":"Z3 hop failed for 96","path":["petList",0,"bark"]}"""
        ];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreatePetSequencedGatewayAsync,
            "{ petList(ids: [96, 93]) { ... on Dog { bark barkNN } } }",
            include: null,
            ["Z2", "Z3"],
            [],
            nulling,
            []);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"petList":null}

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheRaisedErrorOfASharedEntity_When_ASiblingErrorNulledItsPrimaryList()
    {
        // arrange
        const string query = "{ a: userList(ids: [96, 93]) { total badNN } b: userList(ids: [96]) { total } }";
        const string iError = """{"message":"I hop failed for 93","path":["a",1,"badNN"]}""";
        const string bError = """{"message":"C hop failed for sku-96","path":["b",0,"total"]}""";
        const string aError = """{"message":"C hop failed for sku-96","path":["a",0,"total"]}""";

        // act
        var requestInFlight = await ExecuteInEveryHopOrderAsync(
            CreateInFlightRaisedErrorSequencedGatewayAsync,
            query,
            include: null,
            ["B", "C", "I"],
            [("B", "C"), ("B", "I")],
            [iError, aError, bError],
            []);

        // When the list a is null before the hop starts, the entity below a is not a target of the hop.
        var listNullFirst = await ExecuteInEveryHopOrderAsync(
            CreateRaisedErrorSequencedGatewayAsync,
            query,
            include: null,
            ["B", "C", "I"],
            [("I", "B"), ("B", "C")],
            [iError, bError],
            [aError]);

        var sequence = new HopSequence();
        using var inFlightGateway = await CreateInFlightRaisedErrorSequencedGatewayAsync(
            sequence,
            enableRequestGrouping: true);
        sequence.Start(["B", "I", "C"]);
        var inFlightOrder = await ExecuteWithSortedErrorsAsync(inFlightGateway, query, include: null);
        sequence.Start([]);

        // assert
        new[] { requestInFlight, listNullFirst, inFlightOrder }.MatchInlineSnapshots(
        [
            """
            data: {"a":null,"b":[{"total":null}]}

            """,
            """
            data: {"a":null,"b":[{"total":null}]}

            """,
            """
            data: {"a":null,"b":[{"total":null}]}
            errors: [{"message":"C hop failed for sku-96","path":["a",0,"total"]},{"message":"C hop failed for sku-96","path":["b",0,"total"]},{"message":"I hop failed for 93","path":["a",1,"badNN"]}]

            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_ReportTheRaisedErrorOnlyOnTheOtherList_When_ThePrimaryListIsNulledFirst()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateMergeOrderedRaisedErrorSequencedGatewayAsync(sequence);
        sequence.Start(["I", "B", "C"]);

        // act
        var result = await ExecuteWithSortedErrorsAsync(
            gateway,
            "{ a: userList(ids: [96, 93]) { total badNN } b: userList(ids: [96]) { total } }",
            include: null);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"a":null,"b":[{"total":null}]}
            errors: [{"message":"C hop failed for sku-96","path":["b",0,"total"]},{"message":"I hop failed for 93","path":["a",1,"badNN"]}]

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheKeyFetchErrorOnTheDependentField_When_ASiblingErrorNulledTheList()
    {
        // arrange
        string[] nulling =
        [
            """{"message":"B hop failed for 98","path":["userList",0,"totalNN"]}""",
            """{"message":"E hop failed for 93","path":["userList",1,"scoreNN"]}"""
        ];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreateSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { totalNN scoreNN } }",
            include: null,
            ["B", "E"],
            [],
            nulling,
            []);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"userList":null}

            """);
    }

    public static class PetZ1
    {
        [InterfaceType("Pet")]
        public interface IPet
        {
            int Id { get; }
        }

        [EntityKey("id")]
        public record Dog(int Id) : IPet;

        [EntityKey("id")]
        public record Cat(int Id) : IPet;

        public class Query
        {
            [GraphQLType<ListType<NonNullType<InterfaceType<IPet>>>>]
            public IEnumerable<IPet> GetPetList(int[] ids)
                => ids.Select(i => i > 500 ? (IPet)new Cat(i) : new Dog(i));

            [Lookup, Internal]
            public Dog? GetDogById(int id) => new(id);
        }
    }

    public static class PetZ2
    {
        public record Dog(int Id, int BarkNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public Dog? GetDogById(int id)
                => id == 93 ? throw new GraphQLException("Z2 hop failed for 93") : new Dog(id, id);
        }
    }

    public static class PetZ3
    {
        public record Dog(int Id, int? Bark);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public Dog? GetDogById(int id)
                => id == 96 ? throw new GraphQLException("Z3 hop failed for 96") : new Dog(id, id);
        }
    }

    [Fact]
    public async Task Execute_Should_NotReportAnErrorOnAFieldExcludedAtRuntime_When_ASiblingErrorNulledTheItem()
    {
        // arrange
        string[] nulling =
        [
            """{"message":"DN hop failed for 94","path":["nullablePetList",0,"barkNN"]}""",
            """{"message":"DB hop failed for 94","path":["nullablePetList",0,"woof"]}"""
        ];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreateDogSequencedGatewayAsync,
            "query($s: Boolean!) { nullablePetList(ids: [94, 600]) "
            + "{ ... on Dog { bark @include(if: $s) woof barkNN } ... on Cat { bark } } }",
            include: false,
            ["DN", "DB"],
            [],
            nulling,
            []);

        // assert
        result.MatchInlineSnapshot(
            """
            data: {"nullablePetList":[null,{"bark":600}]}

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheKeyFetchErrorOnTheDependentField_When_TheListIsNullBeforeTheHopAnswers()
    {
        // arrange
        const string iError = """{"message":"I hop failed for 93","path":["userList",1,"badNN"]}""";
        string[] orders = ["B", "I"];

        // act
        var total = await ExecuteInEveryHopOrderAsync(
            CreateRaisedErrorSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { total badNN } }",
            include: null,
            orders,
            [],
            [iError, """{"message":"B hop failed for 98","path":["userList",0,"total"]}"""],
            []);
        var totalNN = await ExecuteInEveryHopOrderAsync(
            CreateRaisedErrorSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { totalNN badNN } }",
            include: null,
            orders,
            [],
            [iError, """{"message":"B hop failed for 98","path":["userList",0,"totalNN"]}"""],
            []);
        var middle = await ExecuteInEveryHopOrderAsync(
            CreateRaisedErrorSequencedGatewayAsync,
            "{ userList(ids: [98, 2, 93]) { total badNN } }",
            include: null,
            orders,
            [],
            [
                """{"message":"I hop failed for 93","path":["userList",2,"badNN"]}""",
                """{"message":"B hop failed for 98","path":["userList",0,"total"]}"""
            ],
            []);

        // assert
        new[] { total, totalNN, middle }.MatchInlineSnapshots(
        [
            """
            data: {"userList":null}

            """,
            """
            data: {"userList":null}

            """,
            """
            data: {"userList":null}

            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnFieldsOfTheRuntimeType_When_AnInterfaceHopFails()
    {
        // arrange
        const string query =
            "query($s: Boolean!) { nullablePetList(ids: [94, 600]) "
            + "{ name @include(if: $s) ... on Dog { barkNN } ... on Cat { name } } }";

        // act
        var excluded = await ExecuteInEveryHopOrderAsync(
            CreatePetInterfaceSequencedGatewayAsync,
            query,
            include: false,
            ["DN", "PN"],
            [],
            [],
            []);
        var included = await ExecuteInEveryHopOrderAsync(
            CreatePetInterfaceSequencedGatewayAsync,
            query,
            include: true,
            ["DN", "PN"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(excluded, "s: false", "text")
            .Add(included, "s: true", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfANullKeyObjectOnTheDependentField_When_ItsResolverThrows()
    {
        // arrange
        const string list = "{ userList(ids: [98, 2]) { total } }";
        const string nullableItems = "{ nullableUsers(ids: [98, 2]) { total } }";
        var results = new List<string>();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateNullKeyObjectSequencedGatewayAsync(
                new HopSequence(),
                enableRequestGrouping);
            results.Add(await ExecuteWithSortedErrorsAsync(gateway, list, include: null));
            results.Add(await ExecuteWithSortedErrorsAsync(gateway, nullableItems, include: null));
        }

        // assert
        results.MatchInlineSnapshots(
        [
            """
            data: {"userList":[{"total":null},{"total":2}]}
            errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

            """,
            """
            data: {"nullableUsers":[{"total":null},{"total":2}]}
            errors: [{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

            """,
            """
            data: {"userList":[{"total":null},{"total":2}]}
            errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

            """,
            """
            data: {"nullableUsers":[{"total":null},{"total":2}]}
            errors: [{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

            """
        ]);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfANullKeyObjectOnTheDependentField_When_ASiblingNullsTheList()
    {
        // arrange
        string[] hops = ["B", "I"];

        // act
        var list = await ExecuteInEveryHopOrderAsync(
            CreateNullKeyObjectSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { total badNN } }",
            include: null,
            hops,
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateNullKeyObjectSequencedGatewayAsync,
            "{ nullableUsers(ids: [98, 93]) { total badNN } }",
            include: null,
            hops,
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(list, "userList", "text")
            .Add(nullableItems, "nullableUsers", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnTheDependentField_When_TheNullKeyObjectIsExcluded()
    {
        // arrange
        const string field =
            "query($s: Boolean!) { userList(ids: [98, 2]) { total info @include(if: $s) { sku } } }";
        const string fragment =
            "query($s: Boolean!) { userList(ids: [98, 2]) "
            + "{ total ... on User @include(if: $s) { info { sku } } } }";
        var snapshot = Snapshot.Create();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateNullKeyObjectSequencedGatewayAsync(
                new HopSequence(),
                enableRequestGrouping);
            var name = enableRequestGrouping ? "grouping" : "no grouping";
            var excludedField = await ExecuteWithSortedErrorsAsync(gateway, field, include: false);
            var includedField = await ExecuteWithSortedErrorsAsync(gateway, field, include: true);
            var excludedFragment = await ExecuteWithSortedErrorsAsync(gateway, fragment, include: false);
            snapshot
                .Add(excludedField, $"{name}, field, s: false", "text")
                .Add(includedField, $"{name}, field, s: true", "text")
                .Add(excludedFragment, $"{name}, fragment, s: false", "text");
        }

        foreach (var include in new[] { false, true })
        {
            var nulledList = await ExecuteInEveryHopOrderAsync(
                CreateNullKeyObjectSequencedGatewayAsync,
                "query($s: Boolean!) { userList(ids: [98, 93]) { total badNN info @include(if: $s) { sku } } }",
                include,
                ["B", "I"],
                [],
                [],
                []);
            snapshot.Add(nulledList, $"every order, nulled list, s: {include.ToString().ToLowerInvariant()}", "text");
        }

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfANullKeyObjectAtItsPathInEveryOrder_When_ASiblingNullsTheList()
    {
        // arrange
        string[] hops = ["B", "I"];

        // act
        var field = await ExecuteInEveryHopOrderAsync(
            CreateNullKeyObjectSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { badNN info { sku } } }",
            include: null,
            hops,
            [],
            [],
            []);
        var alias = await ExecuteInEveryHopOrderAsync(
            CreateNullKeyObjectSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { badNN i: info { sku } } }",
            include: null,
            hops,
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(field, "info", "text")
            .Add(alias, "alias", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnTheDependentField_When_ASiblingFillsTheKeyObject()
    {
        // arrange
        string[] hops = ["B", "X"];

        // act
        var result = await ExecuteInEveryHopOrderAsync(
            CreateKeyObjectSiblingSequencedGatewayAsync,
            "{ userList(ids: [98, 2]) { info { label } total } }",
            include: null,
            hops,
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(result, "userList", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFetchedSubFieldsInEveryOrder_When_ASiblingNullsTheList()
    {
        // arrange
        string[] hops = ["B", "X", "I"];

        // act
        var code = await ExecuteInEveryHopOrderAsync(
            CreateKeyObjectSiblingSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { info { code label } total badNN } }",
            include: null,
            hops,
            [],
            [],
            []);
        var internalKey = await ExecuteInEveryHopOrderAsync(
            CreateKeyObjectSiblingSequencedGatewayAsync,
            "{ userList(ids: [98, 93]) { info { label } total badNN } }",
            include: null,
            hops,
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(code, "code and label", "text")
            .Add(internalKey, "label", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFetchedSubFieldsInEveryOrder_When_TheKeyObjectIsAnInterface()
    {
        // arrange
        const string selection = "info { ... on InfoA { code label } }";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateInterfaceKeyObjectSequencedGatewayAsync,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "X"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateInterfaceKeyObjectSequencedGatewayAsync,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportEachErrorOnlyOnItsOwnFieldInEveryOrder_When_TheHopRaisesForSeveralFields()
    {
        // arrange
        const string selection = "info { code label } region";
        var snapshot = Snapshot.Create();

        // act
        foreach (var id in new[] { 98, 97 })
        {
            var intact = await ExecuteInEveryHopOrderAsync(
                CreateRegionKeyObjectSequencedGatewayAsync,
                $"{{ userList(ids: [{id}, 2]) {{ {selection} }} }}",
                include: null,
                ["B", "X"],
                [],
                [],
                []);
            var nulled = await ExecuteInEveryHopOrderAsync(
                CreateRegionKeyObjectSequencedGatewayAsync,
                $"{{ userList(ids: [{id}, 93]) {{ {selection} badNN }} }}",
                include: null,
                ["B", "X", "I"],
                [],
                [],
                []);
            snapshot
                .Add(intact, $"{id}, intact list", "text")
                .Add(nulled, $"{id}, nulled list", "text");
        }

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFetchedSubFieldsInEveryOrder_When_TheOtherSourceReturnsNull()
    {
        // arrange
        const string selection = "info { code label }";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoNull.Query>,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "X"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoNull.Query>,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoNull.Query>,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .Add(nullableItems, "nullable items", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfEachSourceOnItsSubFieldsInEveryOrder_When_BothSourcesRaise()
    {
        // arrange
        const string selection = "info { code label }";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoRaise.Query>,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "X"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoRaise.Query>,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoRaise.Query>,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .Add(nullableItems, "nullable items", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFetchedSubFieldsInEveryOrder_When_TheFieldsAreDeferred()
    {
        // arrange
        const string query = "{ userList(ids: [98, 93]) { ... @defer { info { code label } total badNN } } }";
        var sequence = new HopSequence();
        var results = new List<string>();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateKeyObjectSiblingSequencedGatewayAsync(sequence, enableRequestGrouping);

            foreach (var order in GetHopOrders(["B", "X", "I"], []))
            {
                sequence.Start(order);
                results.Add(
                    GetErrorPaths(await ExecuteDeferredPayloadsAsync(gateway, query), "info failed for 98"));
            }
        }

        sequence.Start([]);

        // assert
        Snapshot.Create()
            .Add(Assert.Single(results.Distinct()), "paths of the errors of B in every order", "text")
            .MatchMarkdownSnapshot();
    }

    // Gets the sorted paths of the errors with the message in the payloads.
    private static string GetErrorPaths(string payloads, string message)
    {
        var paths = new List<string>();

        foreach (var payload in payloads.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var node = JsonNode.Parse(payload)!;
            AddPaths(node["errors"]);

            foreach (var entry in node["incremental"] as JsonArray ?? [])
            {
                AddPaths(entry?["errors"]);
            }

            foreach (var entry in node["completed"] as JsonArray ?? [])
            {
                AddPaths(entry?["errors"]);
            }
        }

        return string.Join("\n", paths.Order(StringComparer.Ordinal)) + "\n";

        void AddPaths(JsonNode? errors)
        {
            foreach (var error in errors as JsonArray ?? [])
            {
                if (error?["message"]?.GetValue<string>() == message)
                {
                    paths.Add(error["path"]!.ToJsonString());
                }
            }
        }
    }

    [Fact]
    public async Task Execute_Should_ReportOnlyTheErrorOfTheRequirementSourceOnTheDependentField_When_BothSourcesRaise()
    {
        // arrange
        const string selection = "info { code label } total";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoRaise.Query>,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "X"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoRaise.Query>,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateInfoSourcesSequencedGatewayAsync<XInfoRaise.Query>,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .Add(nullableItems, "nullable items", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfEachRequirementSourceOnTheDependentField_When_BothSourcesRaise()
    {
        // arrange
        const string selection = "info { code } region total";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateCompositeRequirementSequencedGatewayAsync,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "R"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateCompositeRequirementSequencedGatewayAsync,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "R", "I"],
            [],
            [],
            []);
        var sequence = new HopSequence();
        var dependentErrors = new List<string>();

        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateCompositeRequirementSequencedGatewayAsync(sequence, enableRequestGrouping);

            foreach (var order in GetHopOrders(["B", "R"], []))
            {
                sequence.Start(order);
                dependentErrors.Add(
                    await ExecuteForErrorMessagesAtPathAsync(
                        gateway,
                        $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
                        """["userList",0,"total"]"""));
            }
        }

        sequence.Start([]);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nullableItems, "nullable items", "text")
            .Add(Assert.Single(dependentErrors.Distinct()), "errors of the dependent field in response order", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheSubFieldErrorAtItsPathInEveryOrder_When_TheKeyObjectIsReturned()
    {
        // arrange
        const string selection = "info { code } total";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateCodeErrorSequencedGatewayAsync,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateCodeErrorSequencedGatewayAsync,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateCodeErrorSequencedGatewayAsync,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .Add(nullableItems, "nullable items", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFieldInEveryOrder_When_TheKeyObjectHasSeveralPossibleTypes()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        foreach (var raises in new[] { false, true })
        {
            Func<HopSequence, bool, Task<Gateway>> createGateway = raises
                ? CreateInfoSourcesSequencedGatewayAsync<XInfoInterfaceTwoRaise.Query, XInfoInterfaceTwoRaise.InfoA,
                    XInfoInterfaceTwoRaise.InfoB>
                : CreateInfoSourcesSequencedGatewayAsync<XInfoInterfaceTwo.Query, XInfoInterfaceTwo.InfoA,
                    XInfoInterfaceTwo.InfoB>;
            await AddAbstractKeyObjectResultsAsync(
                snapshot,
                createGateway,
                raises ? "both raise" : "b raises");
        }

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFieldInEveryOrder_When_TheKeyObjectIsAUnion()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddAbstractKeyObjectResultsAsync(snapshot, CreateUnionKeyObjectSequencedGatewayAsync, "b raises");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfTheLookupOnTheFieldInEveryOrder_When_TheLookupRaises()
    {
        // arrange
        const string selection = "info { code label } total";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateLookupErrorSequencedGatewayAsync,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "X"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateLookupErrorSequencedGatewayAsync,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateLookupErrorSequencedGatewayAsync,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .Add(nullableItems, "nullable items", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfADeliveredHopInEveryOrder_When_TheNullReachesTheRoot()
    {
        // arrange
        const string subFieldsQuery = "{ users(ids: [98, 93]) { info { code label } total badNN } }";
        const string fieldQuery = "{ users(ids: [98, 93]) { info { code } badNN } }";
        var sequence = new HopSequence();
        var subFields = new List<string>();
        var field = new List<string>();
        var heldSubFields = new List<string>();
        var heldField = new List<string>();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateKeyObjectSiblingSequencedGatewayAsync(sequence, enableRequestGrouping);

            foreach (var order in GetHopOrders(["B", "X", "I"], [("B", "I")]))
            {
                sequence.Start(order);
                subFields.Add(await ExecuteWithSortedErrorsAsync(gateway, subFieldsQuery, include: null));
            }

            foreach (var order in GetHopOrders(["B", "I"], [("B", "I")]))
            {
                sequence.Start(order);
                field.Add(await ExecuteWithSortedErrorsAsync(gateway, fieldQuery, include: null));
            }

            foreach (var order in GetHopOrders(["B", "X", "I"], [("I", "B")]))
            {
                sequence.Start(order, heldHop: "B");
                heldSubFields.Add(await ExecuteWithSortedErrorsAsync(gateway, subFieldsQuery, include: null));
                sequence.Release();
            }

            foreach (var order in GetHopOrders(["B", "I"], [("I", "B")]))
            {
                sequence.Start(order, heldHop: "B");
                heldField.Add(await ExecuteWithSortedErrorsAsync(gateway, fieldQuery, include: null));
                sequence.Release();
            }
        }

        sequence.Start([]);

        // assert
        Snapshot.Create()
            .Add(Assert.Single(subFields.Distinct()), "sub-fields of two sources", "text")
            .Add(Assert.Single(field.Distinct()), "every sub-field from one source", "text")
            .Add(Assert.Single(heldSubFields.Distinct()), "sub-fields of two sources, B answers after I", "text")
            .Add(Assert.Single(heldField.Distinct()), "every sub-field from one source, B answers after I", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_NeverReportTheErrorAtTheTypename_When_TheKeyObjectSelectsIt()
    {
        // arrange
        var snapshot = Snapshot.Create();
        string[] selections =
        [
            "info { __typename code label }",
            "info { code label __typename }",
            "info { code label }"
        ];

        // act
        foreach (var selection in selections)
        {
            var intact = await ExecuteInEveryHopOrderAsync(
                CreateKeyObjectSiblingSequencedGatewayAsync,
                $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
                include: null,
                ["B", "X"],
                [],
                [],
                []);
            var nulled = await ExecuteInEveryHopOrderAsync(
                CreateKeyObjectSiblingSequencedGatewayAsync,
                $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
                include: null,
                ["B", "X", "I"],
                [],
                [],
                []);
            snapshot
                .Add(intact, $"{selection}, intact list", "text")
                .Add(nulled, $"{selection}, nulled list", "text");
        }

        await AddKeyObjectResultsAsync(
            snapshot,
            CreateKeyObjectSiblingSequencedGatewayAsync,
            "info { __typename label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheSubFieldErrorAtItsPathInEveryOrder_When_TheSourceNullsTheKeyObject()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateNonNullCodeErrorSequencedGatewayAsync,
            "info { code } total",
            ["B"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateNonNullCodeErrorSequencedGatewayAsync,
            "info { code label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheSubFieldErrorAtItsPathInEveryOrder_When_TheSourceNullsANestedKeyObject()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateDetailErrorSequencedGatewayAsync,
            "info { code detail { sku } } total",
            ["B"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateDetailErrorSequencedGatewayAsync,
            "info { code } total",
            ["B"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnTheSubFieldThatRaisedItInEveryOrder_When_SiblingsAreFetched()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateNonNullCodeErrorSequencedGatewayAsync,
            "info { code2 code label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_KeepTheSubFieldsOfTheOtherSourceInEveryOrder_When_TheLookupRaises()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateNullableCodeLookupErrorSequencedGatewayAsync,
            "info { code label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFirstFetchedSubFieldInEveryOrder_When_TheSourceFails()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateNullableCodeServerErrorSequencedGatewayAsync,
            "info { code label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFirstFetchedSubFieldInEveryOrder_When_ANestedSubFieldIsShared()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateMetaSequencedGatewayAsync,
            "info { code meta { a b } }",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateMetaSequencedGatewayAsync,
            "info { meta { a b } code }",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheFieldInEveryOrder_When_TheKeyObjectSelectsOnlyTheTypename()
    {
        // arrange
        var snapshot = Snapshot.Create();
        Func<HopSequence, bool, Task<Gateway>> createGateway =
            CreateInfoSourcesSequencedGatewayAsync<XInfoInterfaceTwo.Query, XInfoInterfaceTwo.InfoA,
                XInfoInterfaceTwo.InfoB>;

        // act
        await AddKeyObjectResultsAsync(snapshot, createGateway, "info { __typename }", ["B"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            createGateway,
            "info { __typename ... on InfoA { label } ... on InfoB { code2 } }",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(snapshot, createGateway, "info { __typename ... on InfoA { label } }", ["X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_PreferANonNullSubFieldInEveryOrder_When_TheSourceRaisesAtTheField()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateOptCodeSequencedGatewayAsync,
            "info { opt code label } total",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateOptCodeSequencedGatewayAsync,
            "info { code opt label } total",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateOptCodeSequencedGatewayAsync,
            "info { opt label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnTheDependentFieldInEveryOrder_When_ARequirementRaises()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(snapshot, CreateSkuErrorSequencedGatewayAsync, "info { code } total", ["B"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateSkuErrorSequencedGatewayAsync,
            "info { code label } total",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(snapshot, CreateSkuErrorSequencedGatewayAsync, "info { code sku } total", ["B"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceWithoutAPathInEveryOrder_When_ItsPathIsNotSelected()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(snapshot, CreateHiddenErrorSequencedGatewayAsync, "info { code }", ["B"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateHiddenErrorSequencedGatewayAsync,
            "info { code meta { a b } detail { d } }",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateHiddenErrorSequencedGatewayAsync,
            "info { meta { b a } code }",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceWithoutAPathInEveryOrder_When_TheSourceFetchedOnlyARequirement()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateSkuHiddenErrorSequencedGatewayAsync,
            "info { label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_KeepTheErrorOffTheObject_When_ARootObjectReportsAnErrorAtAnUnselectedPath()
    {
        // arrange
        var source = CreateSourceSchema("s", b => b.AddQueryType<SProduct.Query>());
        using var gateway = await CreateCompositeSchemaAsync([("s", source)], includeOperationPlan: false);
        var results = new List<string>();

        // act
        foreach (var query in new[]
        {
            "{ product { name details { a } } }",
            "{ product { details { a } } }",
            "{ product { name } }",
            "{ productNN { name } }"
        })
        {
            results.Add(await ExecuteWithSortedErrorsAsync(gateway, query, include: null));
        }

        // assert
        results.MatchInlineSnapshots(
            [
                """
                data: {"product":{"name":"p","details":{"a":"a"}}}

                """,
                """
                data: {"product":{"details":{"a":"a"}}}

                """,
                """
                data: {"product":{"name":"p"}}

                """,
                """
                data: {"productNN":{"name":"p"}}

                """
            ]);
    }

    [Fact]
    public async Task Execute_Should_ReportOnlyTheRequirementSourceErrorInEveryOrder_When_TheOtherSourceRaisesBelow()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateLabelErrorSequencedGatewayAsync,
            "info { code label } total",
            ["B", "X"]);
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateLabelErrorSequencedGatewayAsync,
            "info { label } total",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnALeafOfEachNullableObjectInEveryOrder_When_TheSourceRaises()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateTwoObjectsSequencedGatewayAsync,
            "info { left { x } right { y } label }",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnTheNestedLeafInEveryOrder_When_TheValueTypeHasAFieldOfItsName()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddKeyObjectResultsAsync(
            snapshot,
            CreateTwoObjectsAndXSequencedGatewayAsync,
            "info { left { x } right { y } x }",
            ["B", "X"]);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceOnTheListInEveryOrder_When_TheSourceRaisesAtTheList()
    {
        // arrange
        const string selection = "infos { code label }";

        // act
        var intact = await ExecuteInEveryHopOrderAsync(
            CreateInfoListSequencedGatewayAsync,
            $"{{ userList(ids: [98, 97, 96, 2]) {{ {selection} }} }}",
            include: null,
            ["B", "X"],
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            CreateInfoListSequencedGatewayAsync,
            $"{{ userList(ids: [98, 97, 96, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            CreateInfoListSequencedGatewayAsync,
            $"{{ nullableUsers(ids: [98, 97, 96, 93]) {{ {selection} badNN }} }}",
            include: null,
            ["B", "X", "I"],
            [],
            [],
            []);

        // assert
        Snapshot.Create()
            .Add(intact, "intact list", "text")
            .Add(nulled, "nulled list", "text")
            .Add(nullableItems, "nullable items", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfEachSourceOnItsSubFieldInEveryOrder_When_ARootValueTypeFails()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddRootValueTypeResultsAsync(snapshot, "viewer: Viewer! @shareable @error", "a: String", false, false);
        await AddRootValueTypeResultsAsync(snapshot, "viewer: Viewer! @shareable @error", "a: String!", false, false);
        await AddRootValueTypeResultsAsync(snapshot, "viewer: Viewer! @shareable", "a: String", true, true);
        await AddRootValueTypeResultsAsync(snapshot, "viewer: Viewer @shareable", "a: String", true, true);
        await AddRootValueTypeResultsAsync(snapshot, "viewer: Viewer! @shareable", "a: String!", true, true);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOfANullDependentFieldInEveryOrder_When_TheFieldsAreDeferred()
    {
        // arrange
        const string deferredQuery = "{ userList(ids: [98, 2]) { ... @defer { info { code label } total } } }";
        const string plainQuery = "{ userList(ids: [98, 2]) { info { code label } total } }";
        var sequence = new HopSequence();
        var deferredTotals = new List<string>();
        var plainTotals = new List<string>();

        // act
        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateKeyObjectSiblingSequencedGatewayAsync(sequence, enableRequestGrouping);

            foreach (var order in GetHopOrders(["B", "X"], []))
            {
                sequence.Start(order);
                var deferred = GetFirstTotal(await ExecuteDeferredPayloadsAsync(gateway, deferredQuery));
                sequence.Start(order);
                var plain = GetFirstTotal(await ExecuteDeferredPayloadsAsync(gateway, plainQuery));

                if (deferred.Total is "null")
                {
                    deferredTotals.Add($"total: {deferred.Total}, errors: {deferred.Errors}");
                    plainTotals.Add($"total: {plain.Total}, errors: {plain.Errors}");
                }
            }
        }

        sequence.Start([]);

        // assert
        Assert.Equal(plainTotals, deferredTotals);
        Assert.Single(plainTotals.Distinct()).MatchInlineSnapshot("total: null, errors: info failed for 98");
    }

    // Gets the value of total of the first item and the distinct messages of the errors at its path.
    private static (string Total, string Errors) GetFirstTotal(string payloads)
    {
        var total = "missing";
        var messages = new List<string>();

        foreach (var payload in payloads.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var node = JsonNode.Parse(payload)!;
            AddMessages(node["errors"]);

            if (node["data"]?["userList"]?[0] is JsonObject item
                && item.TryGetPropertyValue("total", out var itemTotal))
            {
                total = itemTotal?.ToJsonString() ?? "null";
            }

            foreach (var entry in node["incremental"] as JsonArray ?? [])
            {
                if (entry?["subPath"]?.ToJsonString() == "[0]"
                    && entry["data"] is JsonObject data
                    && data.TryGetPropertyValue("total", out var value))
                {
                    total = value?.ToJsonString() ?? "null";
                }

                AddMessages(entry?["errors"]);
            }

            foreach (var entry in node["completed"] as JsonArray ?? [])
            {
                AddMessages(entry?["errors"]);
            }
        }

        return (total, string.Join(",", messages.Distinct()));

        void AddMessages(JsonNode? errors)
        {
            foreach (var error in errors as JsonArray ?? [])
            {
                if (error?["path"]?.ToJsonString() == """["userList",0,"total"]""")
                {
                    messages.Add(error["message"]!.GetValue<string>());
                }
            }
        }
    }

    private async Task AddRootValueTypeResultsAsync(
        Snapshot snapshot,
        string viewerOfA,
        string fieldOfA,
        bool aFails,
        bool bFails)
    {
        var viewerOfB = viewerOfA.Replace(" @error", "", StringComparison.Ordinal);
        var schemaA = $"type Query {{ {viewerOfA} }} type Viewer @shareable {{ {fieldOfA} }}";
        var schemaB = $"type Query {{ {viewerOfB} }} type Viewer @shareable {{ b: String }}";
        var sequence = new HopSequence();
        var results = new List<string>();

        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await CreateCompositeSchemaAsync(
                [
                    ("A", CreateSequencedSourceSchema(sequence, "A", schemaA, aFails)),
                    ("B", CreateSequencedSourceSchema(sequence, "B", schemaB, bFails))
                ],
                configureGatewayBuilder: builder =>
                    builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
                includeOperationPlan: false);

            foreach (var order in GetHopOrders(["A", "B"], []))
            {
                sequence.Start(order);
                results.Add(await ExecuteWithSortedErrorsAsync(gateway, "{ viewer { a b } }", include: null));
            }
        }

        sequence.Start([]);
        snapshot.Add(
            Assert.Single(results.Distinct()),
            $"A {viewerOfA} {{ {fieldOfA} }}{(aFails ? " fails" : "")}, B {viewerOfB} {{ b: String }}"
                + (bFails ? " fails" : ""),
            "text");
    }

    // Creates a source schema whose hop is answered in the order of the current run, with a server error when it fails.
    private TestServer CreateSequencedSourceSchema(HopSequence sequence, string hop, string schemaText, bool fails)
    {
        if (fails)
        {
            var failingHop = new FailingSequencedHop(sequence, hop);
            return CreateSourceSchema(hop, schemaText, mockHttpResponse: failingHop.SendAsync);
        }

        var sequencedHop = new SequencedHop(sequence, hop);
        var server = CreateSourceSchema(hop, schemaText, mockHttpResponse: sequencedHop.SendAsync);
        sequencedHop.Connect(server);
        return server;
    }

    private async Task AddKeyObjectResultsAsync(
        Snapshot snapshot,
        Func<HopSequence, bool, Task<Gateway>> createGateway,
        string selection,
        string[] hops)
    {
        var intact = await ExecuteInEveryHopOrderAsync(
            createGateway,
            $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
            include: null,
            hops,
            [],
            [],
            []);
        var nulled = await ExecuteInEveryHopOrderAsync(
            createGateway,
            $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            [.. hops, "I"],
            [],
            [],
            []);
        var nullableItems = await ExecuteInEveryHopOrderAsync(
            createGateway,
            $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
            include: null,
            [.. hops, "I"],
            [],
            [],
            []);
        snapshot
            .Add(intact, $"{selection}, intact list", "text")
            .Add(nulled, $"{selection}, nulled list", "text")
            .Add(nullableItems, $"{selection}, nullable items", "text");
    }

    private Task<Gateway> CreateNonNullCodeErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoNonNullCodeError.Query>(),
            x => x.AddQueryType<XInfoLabel.Query>(),
            includeDependentSource: true);

    private Task<Gateway> CreateNullableCodeLookupErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoNullableCodeLookupError.Query>(),
            x => x.AddQueryType<XInfoLabel.Query>(),
            includeDependentSource: true);

    private Task<Gateway> CreateNullableCodeServerErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoNullableCodeLookupError.Query>(), "B", true),
                ("x", x => x.AddQueryType<XInfoLabel.Query>(), "X", false),
                ("c", c => c.AddQueryType<CNested.Query>(), null, false)
            ]);

    private Task<Gateway> CreateDetailErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoDetailError.Query>(), "B", false),
                ("c", c => c.AddQueryType<CInfoDetailSku.Query>(), null, false)
            ]);

    private Task<Gateway> CreateMetaSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoMeta.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoMeta.Query>(), "X", false)
            ]);

    private Task<Gateway> CreateOptCodeSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoOptCode.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoLabel.Query>(), "X", false),
                ("c", c => c.AddQueryType<CNested.Query>(), null, false)
            ]);

    private Task<Gateway> CreateSkuErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoSkuError.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoLabel.Query>(), "X", false),
                ("c", c => c.AddQueryType<CNested.Query>(), null, false)
            ]);

    private Task<Gateway> CreateHiddenErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoHiddenError.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoMeta.Query>(), "X", false)
            ]);

    private Task<Gateway> CreateSkuHiddenErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoSkuHiddenError.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoLabel.Query>(), "X", false),
                ("c", c => c.AddQueryType<CNested.Query>(), null, false)
            ]);

    private Task<Gateway> CreateLabelErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoNonNullCodeError.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoLabelError.Query>(), "X", false),
                ("c", c => c.AddQueryType<CNested.Query>(), null, false)
            ]);

    private Task<Gateway> CreateTwoObjectsAndXSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoTwoObjects.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoTwoObjectsAndX.Query>(), "X", false)
            ]);

    private Task<Gateway> CreateTwoObjectsSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoTwoObjects.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoTwoObjects.Query>(), "X", false)
            ]);

    private Task<Gateway> CreateInfoListSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateHopSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            [
                ("b", b => b.AddQueryType<BInfoListError.Query>(), "B", false),
                ("x", x => x.AddQueryType<XInfoListLabel.Query>(), "X", false)
            ]);

    // Composes source schema a, the given sources and source schema i, whose hop is I. A source with a hop is
    // answered in the order of the current run, with a server error when it fails.
    private async Task<Gateway> CreateHopSourcesSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping,
        (string Name, Action<IRequestExecutorBuilder> Configure, string? Hop, bool Fails)[] sources)
    {
        var sourceSchemas = new List<(string, TestServer)>
        {
            ("a", CreateSourceSchema("a", b => b.AddQueryType<A.Query>()))
        };

        foreach (var (name, configure, hop, fails) in sources)
        {
            if (hop is null)
            {
                sourceSchemas.Add((name, CreateSourceSchema(name, configure)));
                continue;
            }

            if (fails)
            {
                var failingHop = new FailingSequencedHop(sequence, hop);
                sourceSchemas.Add((name, CreateSourceSchema(name, configure, mockHttpResponse: failingHop.SendAsync)));
                continue;
            }

            var sequencedHop = new SequencedHop(sequence, hop);
            var server = CreateSourceSchema(name, configure, mockHttpResponse: sequencedHop.SendAsync);
            sequencedHop.Connect(server);
            sourceSchemas.Add((name, server));
        }

        var hopI = new SequencedHop(sequence, "I");
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopI.Connect(i);
        sourceSchemas.Add(("i", i));

        return await CreateCompositeSchemaAsync(
            [.. sourceSchemas],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    public static class BInfoOptCode
    {
        public record Info([property: Shareable] string Sku, string Code, string? Opt);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 98
                    ? throw new GraphQLException("info failed for 98")
                    : new Info("sku-" + id, "code-" + id, "opt-" + id);
        }
    }

    public static class BInfoSkuError
    {
        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            [Shareable]
            public string GetSku() => id == 98 ? throw new GraphQLException("sku failed for 98") : "sku-" + id;

            public string? Code => "code-" + id;
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }
    }

    public static class BInfoHiddenError
    {
        [GraphQLName("Meta")]
        public class MetaObj(int id)
        {
            public string? GetA(IResolverContext context) => Report(context, id, "meta", "a-" + id);
        }

        [GraphQLName("Detail")]
        public class DetailObj(int id)
        {
            public string? GetD(IResolverContext context) => Report(context, id, "detail", "d-" + id);
        }

        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            public string? GetCode(IResolverContext context) => Report(context, id, "info", "code-" + id);

            [Shareable]
            public MetaObj GetMeta() => new(id);

            public DetailObj? GetDetail() => new(id);
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }

        // Reports an error at the unselected sibling hidden for the item 98 and returns the value.
        private static string Report(IResolverContext context, int id, string parent, string value)
        {
            if (id == 98)
            {
                context.ReportError(
                    ErrorBuilder.New()
                        .SetMessage($"{parent} hidden failed for 98")
                        .SetPath(context.Path.Parent.Append("hidden"))
                        .Build());
            }

            return value;
        }
    }

    public static class BInfoListError
    {
        [GraphQLName("Info")]
        public class InfoObj(int id, int index)
        {
            public string? GetCode()
                => id == 97 && index == 1 ? throw new GraphQLException("code failed for 97") : $"code-{id}-{index}";
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id)
                => id == 96 ? throw new GraphQLException("B lookup failed for 96") : new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj?[]? GetInfos()
                => id == 98 ? throw new GraphQLException("infos failed for 98") : [new(id, 0), new(id, 1)];
        }
    }

    public static class XInfoListLabel
    {
        [GraphQLName("Info")]
        public class InfoObj(int id, int index)
        {
            public string? Label => $"label-{id}-{index}";
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj?[]? GetInfos() => [new(id, 0), new(id, 1)];
        }
    }

    public static class BInfoNonNullCodeError
    {
        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            [Shareable]
            public string Sku => "sku-" + id;

            public string Code2 => "code2-" + id;

            public string GetCode() => id == 98 ? throw new GraphQLException("code failed for 98") : "code-" + id;
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }
    }

    public static class BInfoNullableCodeLookupError
    {
        public record Info([property: Shareable] string Sku, string? Code);

        public record User(int Id, [property: Shareable] Info? Info);

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
                => id == 98
                    ? throw new GraphQLException("B lookup failed for 98")
                    : new User(id, new Info("sku-" + id, "code-" + id));
        }
    }

    public static class BInfoDetailError
    {
        [GraphQLName("Detail")]
        public class DetailObj(int id)
        {
            [Shareable]
            public string GetSku() => id == 98 ? throw new GraphQLException("sku failed for 98") : "sku-" + id;
        }

        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            public string? Code => "code-" + id;

            [Shareable]
            public DetailObj? GetDetail() => new(id);
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }
    }

    public static class CInfoDetailSku
    {
        public record Detail([property: Shareable] string Sku);

        public record Info([property: Shareable] Detail Detail);

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
            public User? GetUserBySku([Is("info.detail.sku")] string sku)
                => new(new Info(new Detail(sku)), int.Parse(sku[4..]));
        }
    }

    public static class BInfoMeta
    {
        public record Meta(string? A);

        public record Info(string? Code, [property: Shareable] Meta Meta);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 98
                    ? throw new GraphQLException("info failed for 98")
                    : new Info("code-" + id, new Meta("a-" + id));
        }
    }

    public static class XInfoMeta
    {
        public record Meta(string? B);

        public record Info([property: Shareable] Meta Meta);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo() => new(new Meta("b-" + id));
        }
    }

    public static class QCode
    {
        public record User(int Id, [property: Shareable] string? Code);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, "code-" + id);
        }
    }

    public static class RScore
    {
        public record User([property: Shareable] string Code, [property: Shareable] int? Score);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserByCode(string code)
                => code == "code-98" ? throw new GraphQLException("R failed for 98") : new(code, code.Length);
        }
    }

    public static class ZTotal
    {
        public record Info([property: Shareable] string Sku);

        public record User([property: Shareable] Info Info, [property: Shareable] int? Score, int? Total);

        public record KeyInput(string Sku, int Score);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserByKey([Is("{ score, sku: info.sku }")] KeyInput key)
                => new(new Info(key.Sku), key.Score, int.Parse(key.Sku[4..]));
        }
    }

    public static class BInfoSkuHiddenError
    {
        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            [Shareable]
            public string GetSku(IResolverContext context)
            {
                if (id == 98)
                {
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("sku hidden failed for 98")
                            .SetPath(context.Path.Parent.Append("hidden"))
                            .Build());
                }

                return "sku-" + id;
            }
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }
    }

    public static class XInfoLabelError
    {
        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            public string? GetLabel() => id == 98 ? throw new GraphQLException("label failed for 98") : "label-" + id;
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }
    }

    public static class BInfoTwoObjects
    {
        public record Left(string X);

        public record Right(string Y);

        public record Info([property: Shareable] Left? Left, [property: Shareable] Right? Right);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 98
                    ? throw new GraphQLException("info failed for 98")
                    : new Info(new Left("x-" + id), new Right("y-" + id));
        }
    }

    public static class XInfoTwoObjectsAndX
    {
        public record Left(string? Z);

        public record Right(string? W);

        public record Info(string? X, [property: Shareable] Left? Left, [property: Shareable] Right? Right);

        public record User(int Id, [property: Shareable] Info? Info);

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
                => new(id, new Info("info-x-" + id, new Left("z-" + id), new Right("w-" + id)));
        }
    }

    public static class XInfoTwoObjects
    {
        public record Left(string? Z);

        public record Right(string? W);

        public record Info(string? Label, [property: Shareable] Left? Left, [property: Shareable] Right? Right);

        public record User(int Id, [property: Shareable] Info? Info);

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
                => new(id, new Info("label-" + id, new Left("z-" + id), new Right("w-" + id)));
        }
    }

    public static class SProduct
    {
        public class Details
        {
            public string? GetA(IResolverContext context) => Report(context, "details hidden failed", "a");
        }

        public class Product
        {
            public string? GetName(IResolverContext context) => Report(context, "product hidden failed", "p");

            public Details GetDetails() => new();
        }

        public class Query
        {
            public Product? GetProduct() => new();

            [GraphQLType<NonNullType<ObjectType<Product>>>]
            public Product GetProductNN() => new();
        }

        // Reports an error at the unselected sibling hidden and returns the value.
        private static string Report(IResolverContext context, string message, string value)
        {
            context.ReportError(
                ErrorBuilder.New()
                    .SetMessage(message)
                    .SetPath(context.Path.Parent.Append("hidden"))
                    .Build());
            return value;
        }
    }

    private async Task AddAbstractKeyObjectResultsAsync(
        Snapshot snapshot,
        Func<HopSequence, bool, Task<Gateway>> createGateway,
        string name)
    {
        string[] selections =
        [
            "info { ... on InfoA { code label } }",
            "info { ... on InfoA { code label } ... on InfoB { label2 } }"
        ];

        foreach (var selection in selections)
        {
            var intact = await ExecuteInEveryHopOrderAsync(
                createGateway,
                $"{{ userList(ids: [98, 2]) {{ {selection} }} }}",
                include: null,
                ["B", "X"],
                [],
                [],
                []);
            var nulled = await ExecuteInEveryHopOrderAsync(
                createGateway,
                $"{{ userList(ids: [98, 93]) {{ {selection} badNN }} }}",
                include: null,
                ["B", "X", "I"],
                [],
                [],
                []);
            var nullableItems = await ExecuteInEveryHopOrderAsync(
                createGateway,
                $"{{ nullableUsers(ids: [98, 93]) {{ {selection} badNN }} }}",
                include: null,
                ["B", "X", "I"],
                [],
                [],
                []);
            snapshot
                .Add(intact, $"{name}, {selection}, intact list", "text")
                .Add(nulled, $"{name}, {selection}, nulled list", "text")
                .Add(nullableItems, $"{name}, {selection}, nullable items", "text");
        }
    }

    private Task<Gateway> CreateInfoSourcesSequencedGatewayAsync<TXQuery>(
        HopSequence sequence,
        bool enableRequestGrouping)
        where TXQuery : class
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoCode.Query>(),
            x => x.AddQueryType<TXQuery>(),
            includeDependentSource: true);

    private Task<Gateway> CreateInfoSourcesSequencedGatewayAsync<TXQuery, TXInfoA, TXInfoB>(
        HopSequence sequence,
        bool enableRequestGrouping)
        where TXQuery : class
        where TXInfoA : class
        where TXInfoB : class
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoInterfaceTwo.Query>()
                .AddType<BInfoInterfaceTwo.InfoA>()
                .AddType<BInfoInterfaceTwo.InfoB>(),
            x => x.AddQueryType<TXQuery>().AddType<TXInfoA>().AddType<TXInfoB>(),
            includeDependentSource: false);

    private Task<Gateway> CreateUnionKeyObjectSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoUnion.Query>().AddType<BInfoUnion.InfoA>().AddType<BInfoUnion.InfoB>(),
            x => x.AddQueryType<XInfoUnion.Query>().AddType<XInfoUnion.InfoA>().AddType<XInfoUnion.InfoB>(),
            includeDependentSource: false);

    private Task<Gateway> CreateCodeErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoCodeError.Query>(),
            x => x.AddQueryType<XInfoLabel.Query>(),
            includeDependentSource: true);

    private Task<Gateway> CreateLookupErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateInfoSourcesSequencedGatewayAsync(
            sequence,
            enableRequestGrouping,
            b => b.AddQueryType<BInfoLookupError.Query>(),
            x => x.AddQueryType<XInfoLabel.Query>(),
            includeDependentSource: true);

    private async Task<Gateway> CreateCompositeRequirementSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopR = new SequencedHop(sequence, "R");
        var hopI = new SequencedHop(sequence, "I");
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<BInfoCode.Query>(), mockHttpResponse: hopB.SendAsync);
        var r = CreateSourceSchema("r", b => b.AddQueryType<RRegion.Query>(), mockHttpResponse: hopR.SendAsync);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CComposite.Query>());
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopR.Connect(r);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("r", r), ("c", c), ("i", i)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateInfoSourcesSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping,
        Action<IRequestExecutorBuilder> configureB,
        Action<IRequestExecutorBuilder> configureX,
        bool includeDependentSource)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopX = new SequencedHop(sequence, "X");
        var hopI = new SequencedHop(sequence, "I");
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", configureB, mockHttpResponse: hopB.SendAsync);
        var x = CreateSourceSchema("x", configureX, mockHttpResponse: hopX.SendAsync);
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopX.Connect(x);
        hopI.Connect(i);
        var c = includeDependentSource ? CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>()) : null;
        (string, TestServer)[] sourceSchemas = c is null
            ? [("a", a), ("b", b), ("x", x), ("i", i)]
            : [("a", a), ("b", b), ("x", x), ("c", c), ("i", i)];

        return await CreateCompositeSchemaAsync(
            sourceSchemas,
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    public static class BInfoInterfaceTwo
    {
        [InterfaceType("Info")]
        public interface IInfo
        {
            [Shareable]
            string Sku { get; }
        }

        [Shareable]
        public record InfoA(string Sku, string Code) : IInfo;

        [Shareable]
        public record InfoB(string Sku, string Code2) : IInfo;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<InterfaceType<IInfo>>]
            public IInfo? GetInfo()
                => id == 98 ? throw new GraphQLException("info failed for 98") : new InfoA("sku-" + id, "code-" + id);
        }
    }

    public static class XInfoInterfaceTwo
    {
        [InterfaceType("Info")]
        public interface IInfo
        {
            [Shareable]
            string Sku { get; }
        }

        [Shareable]
        public record InfoA(string Sku, string Label) : IInfo;

        [Shareable]
        public record InfoB(string Sku, string Label2) : IInfo;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<InterfaceType<IInfo>>]
            public IInfo? GetInfo() => new InfoA("sku-" + id, "label-" + id);
        }
    }

    public static class XInfoInterfaceTwoRaise
    {
        [InterfaceType("Info")]
        public interface IInfo
        {
            [Shareable]
            string Sku { get; }
        }

        [Shareable]
        public record InfoA(string Sku, string Label) : IInfo;

        [Shareable]
        public record InfoB(string Sku, string Label2) : IInfo;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<InterfaceType<IInfo>>]
            public IInfo? GetInfo()
                => id == 98
                    ? throw new GraphQLException("x info failed for 98")
                    : new InfoA("sku-" + id, "label-" + id);
        }
    }

    public static class BInfoUnion
    {
        [UnionType("InfoU")]
        public interface IInfoU;

        [Shareable]
        public record InfoA(string Code) : IInfoU;

        [Shareable]
        public record InfoB(string Code2) : IInfoU;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<UnionType<IInfoU>>]
            public IInfoU? GetInfo()
                => id == 98 ? throw new GraphQLException("info failed for 98") : new InfoA("code-" + id);
        }
    }

    public static class XInfoUnion
    {
        [UnionType("InfoU")]
        public interface IInfoU;

        [Shareable]
        public record InfoA(string Label) : IInfoU;

        [Shareable]
        public record InfoB(string Label2) : IInfoU;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<UnionType<IInfoU>>]
            public IInfoU? GetInfo() => new InfoA("label-" + id);
        }
    }

    public static class BInfoCodeError
    {
        [GraphQLName("Info")]
        public class InfoObj(int id)
        {
            [Shareable]
            public string Sku => "sku-" + id;

            public string? GetCode() => id == 98 ? throw new GraphQLException("code failed for 98") : "code-" + id;
        }

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public InfoObj? GetInfo() => new(id);
        }
    }

    public static class RRegion
    {
        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public string? GetRegion() => id == 98 ? throw new GraphQLException("region failed for 98") : "eu";
        }
    }

    public static class BInfoLookupError
    {
        public record Info([property: Shareable] string Sku, string Code);

        public record User(int Id, [property: Shareable] Info? Info);

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
                => id == 98
                    ? throw new GraphQLException("B lookup failed for 98")
                    : new User(id, new Info("sku-" + id, "code-" + id));
        }
    }

    public static class XInfoNull
    {
        public record Info([property: Shareable] string Sku, string Label);

        public record User(int Id, [property: Shareable] Info? Info);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, id == 98 ? null : new Info("sku-" + id, "label-" + id));
        }
    }

    public static class XInfoRaise
    {
        public record Info([property: Shareable] string Sku, string Label);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 98
                    ? throw new GraphQLException("x info failed for 98")
                    : new Info("sku-" + id, "label-" + id);
        }
    }

    private async Task<Gateway> CreateInterfaceKeyObjectSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopX = new SequencedHop(sequence, "X");
        var hopI = new SequencedHop(sequence, "I");
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema(
            "b",
            b => b.AddQueryType<BInfoInterface.Query>().AddType<BInfoInterface.InfoA>(),
            mockHttpResponse: hopB.SendAsync);
        var x = CreateSourceSchema(
            "x",
            b => b.AddQueryType<XInfoInterface.Query>().AddType<XInfoInterface.InfoA>(),
            mockHttpResponse: hopX.SendAsync);
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopX.Connect(x);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("x", x), ("i", i)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateRegionKeyObjectSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopX = new SequencedHop(sequence, "X");
        var hopI = new SequencedHop(sequence, "I");
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<BInfoRegion.Query>(), mockHttpResponse: hopB.SendAsync);
        var x = CreateSourceSchema("x", b => b.AddQueryType<XInfoLabel.Query>(), mockHttpResponse: hopX.SendAsync);
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopX.Connect(x);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("x", x), ("i", i)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    public static class BInfoInterface
    {
        [InterfaceType("Info")]
        public interface IInfo
        {
            [Shareable]
            string Sku { get; }
        }

        [Shareable]
        public record InfoA(string Sku, [property: Shareable] string Code) : IInfo;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<InterfaceType<IInfo>>]
            public IInfo? GetInfo()
                => id == 98 ? throw new GraphQLException("info failed for 98") : new InfoA("sku-" + id, "code-" + id);
        }
    }

    public static class XInfoInterface
    {
        [InterfaceType("Info")]
        public interface IInfo
        {
            [Shareable]
            string Sku { get; }
        }

        [Shareable]
        public record InfoA(string Sku, [property: Shareable] string Label) : IInfo;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            [GraphQLType<InterfaceType<IInfo>>]
            public IInfo? GetInfo() => new InfoA("sku-" + id, "label-" + id);
        }
    }

    public static class BInfoRegion
    {
        public record Info([property: Shareable] string Sku, string Code);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 97 ? throw new GraphQLException("info failed for 97") : new Info("sku-" + id, "code-" + id);

            public string? GetRegion()
                => id is 98 or 97 ? throw new GraphQLException("region failed for " + id) : "eu";
        }
    }

    private async Task<Gateway> CreateKeyObjectSiblingSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
    {
        var hopB = new SequencedHop(sequence, "B", deliveredOnResponse: false);
        var hopX = new SequencedHop(sequence, "X", deliveredOnResponse: false);
        var hopI = new SequencedHop(sequence, "I", deliveredOnResponse: false);
        var listener = new HopMergeListener(sequence, new() { ["b"] = "B", ["x"] = "X", ["i"] = "I" });
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<BInfoCode.Query>(), mockHttpResponse: hopB.SendAsync);
        var x = CreateSourceSchema("x", b => b.AddQueryType<XInfoLabel.Query>(), mockHttpResponse: hopX.SendAsync);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopX.Connect(x);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("x", x), ("c", c), ("i", i)],
            configureGatewayBuilder: builder => builder
                .ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping)
                .AddDiagnosticEventListener(_ => listener),
            includeOperationPlan: false);
    }

    public static class BInfoCode
    {
        public record Info([property: Shareable] string Sku, string Code);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 98 ? throw new GraphQLException("info failed for 98") : new Info("sku-" + id, "code-" + id);
        }
    }

    public static class XInfoLabel
    {
        public record Info([property: Shareable] string Sku, string Label);

        public record User(int Id, [property: Shareable] Info? Info);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, new Info("sku-" + id, "label-" + id));
        }
    }

    private async Task<Gateway> CreatePetInterfaceSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
    {
        var hopDn = new SequencedHop(sequence, "DN");
        var hopPn = new SequencedHop(sequence, "PN");
        var p = CreateSourceSchema("p", b => b.AddQueryType<DogP.Query>().AddType<DogP.Dog>().AddType<DogP.Cat>());
        var dn = CreateSourceSchema("dn", b => b.AddQueryType<DogDn.Query>(), mockHttpResponse: hopDn.SendAsync);
        var pn = CreateSourceSchema(
            "pn",
            b => b.AddQueryType<PetPn.Query>().AddType<PetPn.Dog>().AddType<PetPn.Cat>(),
            mockHttpResponse: hopPn.SendAsync);
        hopDn.Connect(dn);
        hopPn.Connect(pn);

        return await CreateCompositeSchemaAsync(
            [("p", p), ("dn", dn), ("pn", pn)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateNullKeyObjectSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopI = new SequencedHop(sequence, "I");
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema(
            "b",
            b => b.AddQueryType<BNullableInfo.Query>(),
            mockHttpResponse: hopB.SendAsync);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("i", i)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    public static class PetPn
    {
        [InterfaceType("Pet")]
        public interface IPet
        {
            int Id { get; }

            int? Name { get; }
        }

        public record Dog(int Id, int? Name) : IPet;

        public record Cat(int Id, int? Name) : IPet;

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            [GraphQLType<InterfaceType<IPet>>]
            public IPet? GetPetById(int id)
                => id == 94
                    ? throw new GraphQLException("PN hop failed for " + id)
                    : id > 500 ? new Cat(id, id) : new Dog(id, id);
        }
    }

    public static class BNullableInfo
    {
        public record Info([property: Shareable] string Sku);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public Info? GetInfo()
                => id == 98 ? throw new GraphQLException("info failed for 98") : new Info("sku-" + id);
        }
    }

    private async Task<Gateway> CreateDogSequencedGatewayAsync(HopSequence sequence, bool enableRequestGrouping)
    {
        var hopDn = new SequencedHop(sequence, "DN");
        var hopDb = new SequencedHop(sequence, "DB");
        var p = CreateSourceSchema("p", b => b.AddQueryType<DogP.Query>().AddType<DogP.Dog>().AddType<DogP.Cat>());
        var dn = CreateSourceSchema("dn", b => b.AddQueryType<DogDn.Query>(), mockHttpResponse: hopDn.SendAsync);
        var db = CreateSourceSchema("db", b => b.AddQueryType<DogDb.Query>(), mockHttpResponse: hopDb.SendAsync);
        var cb = CreateSourceSchema("cb", b => b.AddQueryType<DogCb.Query>());
        hopDn.Connect(dn);
        hopDb.Connect(db);

        return await CreateCompositeSchemaAsync(
            [("p", p), ("dn", dn), ("db", db), ("cb", cb)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    public static class DogP
    {
        [InterfaceType("Pet")]
        public interface IPet
        {
            int Id { get; }
        }

        [EntityKey("id")]
        public record Dog(int Id) : IPet;

        [EntityKey("id")]
        public record Cat(int Id) : IPet;

        public class Query
        {
            [GraphQLType<ListType<InterfaceType<IPet>>>]
            public IEnumerable<IPet> GetNullablePetList(int[] ids)
                => ids.Select(i => i > 500 ? (IPet)new Cat(i) : new Dog(i));

            [Lookup, Internal]
            public Dog? GetDogById(int id) => new(id);

            [Lookup, Internal]
            public Cat? GetCatById(int id) => new(id);
        }
    }

    public static class DogDn
    {
        public record Dog(int Id, int BarkNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public Dog? GetDogById(int id)
                => id is 93 or 94 ? throw new GraphQLException("DN hop failed for " + id) : new Dog(id, id);
        }
    }

    public static class DogDb
    {
        public record Dog(int Id, int? Bark, int? Woof);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public Dog? GetDogById(int id)
                => id is 94 or 96 ? throw new GraphQLException("DB hop failed for " + id) : new Dog(id, id, id);
        }
    }

    public static class DogCb
    {
        public record Cat(int Id, int? Bark);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public Cat? GetCatById(int id) => new(id, id);
        }
    }

    private Task<Gateway> CreateRaisedErrorSequencedGatewayAsync(HopSequence sequence, bool enableRequestGrouping)
        => CreateRaisedErrorSequencedGatewayAsync(sequence, enableRequestGrouping, hopIWaitsForRequestOfC: false);

    // Answers hop I only after the request of hop C was sent, so the target of hop C is in flight when I nulls it.
    private Task<Gateway> CreateInFlightRaisedErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping)
        => CreateRaisedErrorSequencedGatewayAsync(sequence, enableRequestGrouping, hopIWaitsForRequestOfC: true);

    private async Task<Gateway> CreateRaisedErrorSequencedGatewayAsync(
        HopSequence sequence,
        bool enableRequestGrouping,
        bool hopIWaitsForRequestOfC)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopC = new SequencedHop(sequence, "C");
        var hopI = new SequencedHop(sequence, "I", hopIWaitsForRequestOfC ? "C" : null);
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: hopB.SendAsync);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>(), mockHttpResponse: hopC.SendAsync);
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopC.Connect(c);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("i", i)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreatePetSequencedGatewayAsync(HopSequence sequence, bool enableRequestGrouping)
    {
        var hopZ2 = new SequencedHop(sequence, "Z2");
        var hopZ3 = new SequencedHop(sequence, "Z3");
        var z1 = CreateSourceSchema(
            "z1",
            b => b.AddQueryType<PetZ1.Query>().AddType<PetZ1.Dog>().AddType<PetZ1.Cat>());
        var z2 = CreateSourceSchema("z2", b => b.AddQueryType<PetZ2.Query>(), mockHttpResponse: hopZ2.SendAsync);
        var z3 = CreateSourceSchema("z3", b => b.AddQueryType<PetZ3.Query>(), mockHttpResponse: hopZ3.SendAsync);
        hopZ2.Connect(z2);
        hopZ3.Connect(z3);

        return await CreateCompositeSchemaAsync(
            [("z1", z1), ("z2", z2), ("z3", z3)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateMergeOrderedRaisedErrorSequencedGatewayAsync(HopSequence sequence)
    {
        var hopB = new SequencedHop(sequence, "B", deliveredOnResponse: false);
        var hopC = new SequencedHop(sequence, "C", deliveredOnResponse: false);
        var hopI = new SequencedHop(sequence, "I", deliveredOnResponse: false);
        var listener = new HopMergeListener(sequence, new() { ["b"] = "B", ["c"] = "C", ["i"] = "I" });
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: hopB.SendAsync);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>(), mockHttpResponse: hopC.SendAsync);
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>(), mockHttpResponse: hopI.SendAsync);
        hopB.Connect(b);
        hopC.Connect(c);
        hopI.Connect(i);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("i", i)],
            configureGatewayBuilder: builder => builder
                .ModifyPlannerOptions(o => o.EnableRequestGrouping = true)
                .AddDiagnosticEventListener(_ => listener),
            includeOperationPlan: false);
    }

    private async Task<string> ExecuteKeyHopRaceAsync(string[] order)
    {
        var sequence = new HopSequence();
        var hopQ = new MarkerHop(sequence, ["userById", "accountById"], fail: false);
        var hopK = new MarkerHop(sequence, ["userBySku", "accountByCode"], fail: true);
        var listener = new MarkerMergeListener(sequence, ["userById", "userBySku", "accountById", "accountByCode"]);
        var p = CreateSourceSchema("p", b => b.AddQueryType<KeyP.Query>());
        var q = CreateSourceSchema("q", b => b.AddQueryType<KeyQ.Query>(), mockHttpResponse: hopQ.SendAsync);
        var k = CreateSourceSchema("k", b => b.AddQueryType<KeyK.Query>(), mockHttpResponse: hopK.SendAsync);
        hopQ.Connect(q);
        hopK.Connect(k);
        using var gateway = await CreateCompositeSchemaAsync(
            [("p", p), ("q", q), ("k", k)],
            configureGatewayBuilder: builder => builder
                .ModifyPlannerOptions(o => o.EnableRequestGrouping = false)
                .AddDiagnosticEventListener(_ => listener),
            includeOperationPlan: false);

        sequence.Start(order);
        var result = await ExecuteWithSortedErrorsAsync(
            gateway,
            "{ users(ids: [2, 3]) { name kayNN } nullableAccounts(ids: [5]) { kayNN } }",
            include: null);
        sequence.Start([]);

        return result;
    }

    // Answers the operation of a marker in the order of the current run. A MarkerMergeListener delivers it.
    private sealed class MarkerHop(HopSequence sequence, string[] markers, bool fail)
    {
        private HttpMessageInvoker? _invoker;

        public void Connect(TestServer server)
            => _invoker = new HttpMessageInvoker(server.CreateHandler());

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var hop = markers.First(m => body.Contains(m, StringComparison.Ordinal));

            await sequence.WaitForTurnAsync(hop, cancellationToken);

            return fail
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : await _invoker!.SendAsync(request, cancellationToken);
        }
    }

    // Delivers the hops of the markers in the operations of a node once the node has queued its results for merging
    // or recorded its errors.
    private sealed class MarkerMergeListener(HopSequence sequence, string[] markers)
        : FusionExecutionDiagnosticEventListener
    {
        public override IDisposable ExecuteOperationNode(
            OperationPlanContext context,
            OperationExecutionNode node,
            string schemaName)
            => TryDeliver([GetText(node.Operation)]) ?? base.ExecuteOperationNode(context, node, schemaName);

        public override IDisposable ExecuteOperationBatchNode(
            OperationPlanContext context,
            OperationBatchExecutionNode node,
            string schemaName)
        {
            var texts = new List<string>();

            foreach (var operation in node.Operations)
            {
                texts.Add(GetText(operation.SourceText));
            }

            return TryDeliver(texts) ?? base.ExecuteOperationBatchNode(context, node, schemaName);
        }

        private Delivery? TryDeliver(List<string> texts)
        {
            var hops = markers.Where(m => texts.Any(t => t.Contains(m, StringComparison.Ordinal))).ToArray();
            return hops.Length == 0 ? null : new Delivery(sequence, hops);
        }

        private static string GetText(OperationSourceText operation) => Encoding.UTF8.GetString(operation.Value.Span);

        private sealed class Delivery(HopSequence sequence, string[] hops) : IDisposable
        {
            public void Dispose()
            {
                foreach (var hop in hops)
                {
                    sequence.MarkDelivered(hop);
                }
            }
        }
    }

    public static class KeyP
    {
        [EntityKey("id")]
        public record User(int Id, string Name);

        [EntityKey("id")]
        public record Account(int Id);

        public class Query
        {
            public IEnumerable<User> GetUsers(int[] ids) => ids.Select(i => new User(i, $"user-{i}"));

            public IEnumerable<Account?> GetNullableAccounts(int[] ids) => ids.Select(i => new Account(i));

            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, $"user-{id}");

            [Lookup, Internal]
            public Account? GetAccountById(int id) => new(id);
        }
    }

    public static class KeyQ
    {
        public record Info([property: Shareable] string Sku);

        public record User(int Id, [property: Shareable] Info Info);

        public record Account(int Id, [property: Shareable] string Code);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, new Info($"sku-{id}"));

            [Lookup, Internal]
            public Account? GetAccountById(int id) => new(id, $"code-{id}");
        }
    }

    public static class KeyK
    {
        public record Info([property: Shareable] string Sku);

        public record User([property: Shareable] Info Info, int KayNN);

        public record Account([property: Shareable] string Code, int KayNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserBySku([Is("info.sku")] string sku) => new(new Info(sku), 2);

            [Lookup, Internal]
            public Account? GetAccountByCode(string code) => new(code, 3);
        }
    }

    // Whether a hop that has not started runs below a null value depends on timing. For results below a nulled
    // ancestor the data is identical, the errors that nulled it appear in every order, every error appears at most
    // once, and every other error is one of the allowed errors with exactly its message and path.
    private static void AssertNulledResults(
        IEnumerable<string> results,
        string[] nullingErrors,
        string[] allowedErrors)
    {
        var list = results.ToList();
        var datas = list.Select(r => r.Split('\n')[0]).Distinct().ToList();
        Assert.Single(datas);

        foreach (var result in list)
        {
            var line = result.Split('\n').First(l => l.StartsWith("errors:", StringComparison.Ordinal));
            using var document = JsonDocument.Parse(line["errors:".Length..]);
            var errors = document.RootElement.EnumerateArray().Select(e => e.GetRawText()).ToList();

            Assert.Equal(errors.Count, errors.Distinct().Count());
            Assert.All(nullingErrors, e => Assert.Contains(e, errors));
            Assert.All(errors, e => Assert.True(
                nullingErrors.Contains(e) || allowedErrors.Contains(e),
                $"Unexpected error below a null value: {e}"));
        }
    }

    [Fact]
    public void AssertNulledResults_Should_Throw_When_AnExtraErrorHasAWrongPath()
    {
        // arrange
        const string root = """{"message":"m","path":["users",0,"kayNN"]}""";
        const string extra = """{"message":"m","path":["nullableAccounts",0,"kayNN"]}""";
        const string wrong = """{"message":"m","path":["nullableAccounts",1,"kayNN"]}""";

        // act
        AssertNulledResults([$"data: null\nerrors: [{root},{extra}]\n"], [root], [extra]);

        // assert
        Assert.ThrowsAny<Exception>(
            () => AssertNulledResults([$"data: null\nerrors: [{root},{wrong}]\n"], [root], [extra]));
    }

    private async Task<string> ExecuteInEveryHopOrderAsync(
        Func<HopSequence, bool, Task<Gateway>> createGateway,
        string query,
        bool? include,
        string[] hops,
        (string First, string Second)[] precedences,
        string[] nullingErrors,
        string[] allowedErrors)
    {
        var sequence = new HopSequence();
        var results = new List<string>();

        foreach (var enableRequestGrouping in new[] { true, false })
        {
            using var gateway = await createGateway(sequence, enableRequestGrouping);

            foreach (var order in GetHopOrders(hops, precedences))
            {
                sequence.Start(order);
                results.Add(await ExecuteWithSortedErrorsAsync(gateway, query, include));
            }
        }

        sequence.Start([]);

        if (nullingErrors.Length > 0 || results.Any(r => r.StartsWith("data: null", StringComparison.Ordinal)))
        {
            AssertNulledResults(results, nullingErrors, allowedErrors);
            return results[0].Split('\n')[0] + "\n";
        }

        return Assert.Single(results.Distinct());
    }

    private static List<string[]> GetHopOrders(string[] hops, (string First, string Second)[] precedences)
    {
        var orders = new List<string[]>();
        var current = new List<string>();

        void Extend()
        {
            if (current.Count == hops.Length)
            {
                orders.Add([.. current]);
                return;
            }

            foreach (var hop in hops)
            {
                if (current.Contains(hop)
                    || precedences.Any(p => p.Second == hop && !current.Contains(p.First)))
                {
                    continue;
                }

                current.Add(hop);
                Extend();
                current.RemoveAt(current.Count - 1);
            }
        }

        Extend();
        return orders;
    }

    // Returns the messages of the errors at the path, in the order of the response.
    private static async Task<string> ExecuteForErrorMessagesAtPathAsync(Gateway gateway, string query, string path)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.PostAsync(
            new OperationRequest(query),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        using var result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);

        foreach (var error in result.Errors.EnumerateArray())
        {
            if (error.TryGetProperty("path", out var errorPath) && errorPath.GetRawText() == path)
            {
                text.AppendLine(error.GetProperty("message").GetString());
            }
        }

        return text.ToString();
    }

    // Returns the message and path of every error, in the order of the response.
    private static async Task<string> ExecuteForErrorsInResponseOrderAsync(Gateway gateway, string query)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.PostAsync(
            new OperationRequest(query),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        using var result = await response.ReadAsResultAsync(TestContext.Current.CancellationToken);

        foreach (var error in result.Errors.EnumerateArray())
        {
            var path = error.TryGetProperty("path", out var errorPath) ? errorPath.GetRawText() : "no path";
            text.AppendLine($"{error.GetProperty("message").GetString()} at {path}");
        }

        return text.ToString();
    }

    private static async Task<string> ExecuteWithSortedErrorsAsync(Gateway gateway, string query, bool? include)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var request = include is null
            ? new OperationRequest(query)
            : new OperationRequest(query, variables: new Dictionary<string, object?> { ["s"] = include.Value });
        using var response = await client.PostAsync(
            request,
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        await foreach (var result in response.ReadAsResultStreamAsync())
        {
            text.AppendLine($"data: {result.Data.GetRawText()}");

            if (result.Errors.ValueKind is not JsonValueKind.Undefined)
            {
                var errors = result.Errors.EnumerateArray().Select(e => e.GetRawText()).Order(StringComparer.Ordinal);
                text.AppendLine($"errors: [{string.Join(",", errors)}]");
            }
        }

        return text.ToString();
    }

    private async Task<Gateway> CreateSequencedGatewayAsync(HopSequence sequence, bool enableRequestGrouping)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopC = new SequencedHop(sequence, "C");
        var hopD = new SequencedHop(sequence, "D");
        var hopE = new SequencedHop(sequence, "E");
        var hopF = new SequencedHop(sequence, "F");

        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: hopB.SendAsync);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>(), mockHttpResponse: hopC.SendAsync);
        var d = CreateSourceSchema("d", b => b.AddQueryType<D.Query>(), mockHttpResponse: hopD.SendAsync);
        var e = CreateSourceSchema("e", b => b.AddQueryType<E.Query>(), mockHttpResponse: hopE.SendAsync);
        var f = CreateSourceSchema("f", b => b.AddQueryType<F.Query>(), mockHttpResponse: hopF.SendAsync);

        hopB.Connect(b);
        hopC.Connect(c);
        hopD.Connect(d);
        hopE.Connect(e);
        hopF.Connect(f);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("d", d), ("e", e), ("f", f)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private sealed class HopSequence
    {
        private HopRun _run = new([], heldHop: null);

        // Starts a run that delivers the hops in the given order and holds the held hop until Release is called.
        public void Start(string[] order, string? heldHop = null) => _run = new HopRun(order, heldHop);

        public HopRun Current => _run;

        public void Release() => _run.Release();

        public Task WaitForTurnAsync(string hop, CancellationToken cancellationToken)
            => _run.WaitForTurnAsync(hop, cancellationToken);

        public void MarkDelivered(string hop) => _run.MarkDelivered(hop);
    }

    private sealed class HopRun(string[] order, string? heldHop)
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        private readonly TaskCompletionSource[] _delivered = order
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _requested = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkRequested(string hop) => GetRequested(hop).TrySetResult();

        public Task WaitForRequestAsync(string hop, CancellationToken cancellationToken)
            => GetRequested(hop).Task.WaitAsync(s_timeout, cancellationToken);

        public async Task WaitForTurnAsync(string hop, CancellationToken cancellationToken)
        {
            if (hop == heldHop)
            {
                await _released.Task.WaitAsync(s_timeout, cancellationToken);
            }

            var index = Array.IndexOf(order, hop);

            if (index > 0)
            {
                await _delivered[index - 1].Task.WaitAsync(s_timeout, cancellationToken);
            }
        }

        public void MarkDelivered(string hop)
        {
            var index = Array.IndexOf(order, hop);

            if (index >= 0)
            {
                _delivered[index].TrySetResult();
            }
        }

        public void Release() => _released.TrySetResult();

        private TaskCompletionSource GetRequested(string hop)
            => _requested.GetOrAdd(
                hop,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    // A hop that is answered in the order of the current run, after the request of waitForRequestOf was sent. It is
    // delivered once its response is read, or, when deliveredOnResponse is false, once a HopMergeListener reports it.
    private sealed class SequencedHop(
        HopSequence sequence,
        string hop,
        string? waitForRequestOf = null,
        bool deliveredOnResponse = true)
    {
        private HttpMessageInvoker? _invoker;

        public void Connect(TestServer server)
            => _invoker = new HttpMessageInvoker(server.CreateHandler());

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var run = sequence.Current;
            run.MarkRequested(hop);

            if (waitForRequestOf is not null)
            {
                await run.WaitForRequestAsync(waitForRequestOf, cancellationToken);
            }

            await run.WaitForTurnAsync(hop, cancellationToken);
            var response = await _invoker!.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync();

            if (deliveredOnResponse)
            {
                run.MarkDelivered(hop);
            }

            return response;
        }
    }

    // Delivers the hop of a source schema in the run that was current when its node started, once the node has
    // queued its result for merging, so that the result is merged before the result of any hop answered after it.
    private sealed class HopMergeListener(HopSequence sequence, Dictionary<string, string> hopsBySchema)
        : FusionExecutionDiagnosticEventListener
    {
        public override IDisposable ExecuteOperationNode(
            OperationPlanContext context,
            OperationExecutionNode node,
            string schemaName)
            => TryDeliver(schemaName) ?? base.ExecuteOperationNode(context, node, schemaName);

        public override IDisposable ExecuteOperationBatchNode(
            OperationPlanContext context,
            OperationBatchExecutionNode node,
            string schemaName)
            => TryDeliver(schemaName) ?? base.ExecuteOperationBatchNode(context, node, schemaName);

        private Delivery? TryDeliver(string schemaName)
            => hopsBySchema.TryGetValue(schemaName, out var hop) ? new Delivery(sequence.Current, hop) : null;

        private sealed class Delivery(HopRun run, string hop) : IDisposable
        {
            public void Dispose() => run.MarkDelivered(hop);
        }
    }

    // A hop that is answered with a server error in the order of the current run.
    private sealed class FailingSequencedHop(HopSequence sequence, string hop)
    {
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            var run = sequence.Current;
            run.MarkRequested(hop);
            await run.WaitForTurnAsync(hop, TestContext.Current.CancellationToken);
            run.MarkDelivered(hop);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private static Task<HttpResponseMessage> ServerError(HttpRequestMessage request)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    private static async Task<HttpResponseMessage> GatedServerError(HttpRequestMessage request)
    {
        await (ArrivalGate.Current?.BeforeFailureAsync() ?? Task.CompletedTask);
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        ArrivalGate.Current?.FailureSent();
        return response;
    }

    // Forces which skip reaches the executor first. Either the failing hop is answered before the accounts root is
    // released, or the failing hop is answered only after the conditioned node of the accounts root has completed.
    internal sealed class ArrivalGate(bool failureArrivesFirst)
    {
        private readonly TaskCompletionSource _failureSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _conditionSkipped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static ArrivalGate? Current { get; set; }

        public async Task BeforeFailureAsync()
        {
            if (!failureArrivesFirst)
            {
                await _conditionSkipped.Task;

                // The node completes right after its scope is disposed, the executor reads the result shortly after.
                await Task.Delay(100);
            }
        }

        public void FailureSent() => _failureSent.TrySetResult();

        public Task BeforeAccountsAsync() => failureArrivesFirst ? _failureSent.Task : Task.CompletedTask;

        public void ConditionSkipped() => _conditionSkipped.TrySetResult();
    }

    internal sealed class ConditionSkipListener : FusionExecutionDiagnosticEventListener
    {
        public override IDisposable ExecuteOperationNode(
            OperationPlanContext context,
            OperationExecutionNode node,
            string schemaName)
            => schemaName == "e" ? new Signal() : base.ExecuteOperationNode(context, node, schemaName);

        public override IDisposable ExecuteApolloOperationExecutionNode(
            OperationPlanContext context,
            ApolloOperationExecutionNode node,
            string schemaName)
            => schemaName == "e" ? new Signal() : base.ExecuteApolloOperationExecutionNode(context, node, schemaName);

        private sealed class Signal : IDisposable
        {
            public void Dispose() => ArrivalGate.Current?.ConditionSkipped();
        }
    }

    private static async Task<HttpResponseMessage> ServerErrorAfterDelayB(HttpRequestMessage request)
    {
        await Task.Delay(s_delayB);
        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    private async Task<Gateway> CreateFiveSchemaGatewayAsync(
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockMiddleHop = null,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockCodeHop = null)
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: mockMiddleHop);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var d = CreateSourceSchema("d", b => b.AddQueryType<D.Query>());
        var e = CreateSourceSchema("e", b => b.AddQueryType<E.Query>(), mockHttpResponse: mockCodeHop);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("d", d), ("e", e)],
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateThreeHopGatewayAsync()
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var e = CreateSourceSchema("e", b => b.AddQueryType<E.Query>());
        var d = CreateSourceSchema("d", b => b.AddQueryType<D.Query>());
        var f = CreateSourceSchema("f", b => b.AddQueryType<F.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("e", e), ("d", d), ("f", f)],
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateTwoHopsOnTheSameTargetGatewayAsync()
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>());
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var h = CreateSourceSchema("h", b => b.AddQueryType<H.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("h", h)],
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateRaisedErrorGatewayAsync()
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>());
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var i = CreateSourceSchema("i", b => b.AddQueryType<I.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c), ("i", i)],
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateOneHopMutationGatewayAsync()
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>().AddMutationType<A.Mutation>());
        var j = CreateSourceSchema("j", b => b.AddQueryType<J.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("j", j)],
            includeOperationPlan: false);
    }

    private static async Task<string> ExecuteMutationAsync(Gateway gateway, string query)
    {
        s_calls.Clear();
        var text = new StringBuilder(await ExecuteQueryAsync(gateway, query));
        text.AppendLine($"calls: {string.Join(" ", s_calls.Order(StringComparer.Ordinal))}");
        return text.ToString();
    }

    private async Task<Gateway> CreateBatchedGatewayAsync(bool enableRequestGrouping)
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<BatchA.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: ServerError);
        var e = CreateSourceSchema("e", b => b.AddQueryType<BatchE.Query>());
        var k = CreateSourceSchema("k", b => b.AddQueryType<BatchK.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("e", e), ("k", k)],
            configureGatewayBuilder: builder =>
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateSplitBatchedGatewayAsync(bool enableRequestGrouping)
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<SplitUsers.Query>());
        var accounts = CreateSourceSchema("accounts", b => b.AddQueryType<SplitAccounts.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: GatedServerError);
        var e = CreateSourceSchema("e", b => b.AddQueryType<BatchE.Query>());
        var k = CreateSourceSchema("k", b => b.AddQueryType<BatchK.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("accounts", accounts), ("b", b), ("e", e), ("k", k)],
            configureGatewayBuilder: builder =>
            {
                builder.ModifyPlannerOptions(o => o.EnableRequestGrouping = enableRequestGrouping);
                builder.AddDiagnosticEventListener(_ => new ConditionSkipListener());
            },
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateOneHopGatewayAsync()
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var j = CreateSourceSchema("j", b => b.AddQueryType<J.Query>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("j", j)],
            includeOperationPlan: false);
    }

    private Task<Gateway> CreateGatewayAsync(
        Type lastHopType,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockLastHop = null,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockMiddleHop = null)
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: mockMiddleHop);
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

    private static async Task<List<string>> ExecuteInEveryOrderAsync(Gateway gateway, string query)
    {
        (int B, int C, int D, int E, int H)[] orders =
        [
            (0, 0, 0, 0, 0),
            (200, 0, 0, 0, 0),
            (0, 200, 0, 0, 0),
            (0, 0, 200, 0, 0),
            (0, 0, 0, 200, 0),
            (0, 0, 0, 0, 200),
            (200, 0, 0, 400, 0),
            (400, 0, 0, 200, 200)
        ];
        var results = new List<string>();

        try
        {
            foreach (var (b, c, d, e, h) in orders)
            {
                (s_delayB, s_delayC, s_delayD, s_delayE, s_delayH) = (b, c, d, e, h);
                results.Add(await ExecuteQueryAsync(gateway, query));
            }
        }
        finally
        {
            (s_delayB, s_delayC, s_delayD, s_delayE, s_delayH) = (0, 0, 0, 0, 0);
        }

        return results;
    }

    private static async Task<string> ExecuteDeferredPayloadsAsync(Gateway gateway, string query)
    {
        using var client = gateway.CreateClient();
        using var content = new StringContent(
            JsonSerializer.Serialize(new { query }),
            Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/graphql")
        {
            Content = content
        };
        request.Headers.Add("Accept", "multipart/mixed; deferSpec=20220824, application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var payloads = body
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('{'));

        return string.Join("\n", payloads) + "\n";
    }

    private static async Task<string> ExecuteQueryAsync(Gateway gateway, string query, bool condition)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.PostAsync(
            new OperationRequest(query, variables: new Dictionary<string, object?> { ["s"] = condition }),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        await foreach (var result in response.ReadAsResultStreamAsync())
        {
            AppendResult(text, result, includeErrors: true);
        }

        return text.ToString();
    }

    private static async Task<string> ExecuteQueryAsync(Gateway gateway, string query)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.PostAsync(
            new OperationRequest(query),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        await foreach (var result in response.ReadAsResultStreamAsync())
        {
            AppendResult(text, result, includeErrors: true);
        }

        return text.ToString();
    }

    private static async Task<string> ExecuteAsync(
        Gateway gateway,
        string query,
        params int[][] idSets)
    {
        var text = new StringBuilder();
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        foreach (var ids in idSets)
        {
            var list = string.Join(", ", ids);
            s_calls.Clear();
            using var response = await client.PostAsync(
                new OperationRequest(query.Replace("$ids", list, StringComparison.Ordinal)),
                new Uri("http://localhost:5000/graphql"),
                TestContext.Current.CancellationToken);

            text.AppendLine($"## ids [{list}]");

            await foreach (var result in response.ReadAsResultStreamAsync())
            {
                AppendResult(text, result, includeErrors: true);
            }

            text.AppendLine($"lookups: {string.Join(" ", s_calls.Order(StringComparer.Ordinal))}");
        }

        return text.ToString();
    }

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

            var entryErrors = result.Incremental.EnumerateArray()
                .Where(entry => entry.TryGetProperty("errors", out _))
                .SelectMany(entry => entry.GetProperty("errors").EnumerateArray())
                .Select(error => error.GetRawText())
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();

            if (entryErrors.Count > 0)
            {
                text.AppendLine($"incremental errors: [{string.Join(",", entryErrors)}]");
            }
        }

        if (includeErrors && result.Errors.ValueKind is not JsonValueKind.Undefined)
        {
            text.AppendLine($"errors: {result.Errors.GetRawText()}");
        }
    }

    public static class A
    {
        [EntityKey("id")]
        public record User(int Id, string Name, [property: Shareable] string? Nick);

        public class Query
        {
            public IEnumerable<User> GetUsers(int[] ids) => ids.Select(Create);

            public IEnumerable<User?> GetNullableUsers(int[] ids) => ids.Select(Create);

            [GraphQLType<ListType<NonNullType<ObjectType<User>>>>]
            public IEnumerable<User> GetUserList(int[] ids) => ids.Select(Create);

            public User? GetUser(int id) => Create(id);

            [Lookup, Internal]
            public User? GetUserById(int id) => Create(id);

            private static User Create(int id) => new(id, $"user-{id}", id == 94 ? null : $"nick-{id}");
        }

        public class Mutation
        {
            public User? CreateUser(int id)
            {
                s_calls.Enqueue("A:create-" + id);
                return new User(id, $"user-{id}", id == 94 ? null : $"nick-{id}");
            }

            [GraphQLType<NonNullType<ObjectType<User>>>]
            public User CreateUserNN(int id)
            {
                s_calls.Enqueue("A:create-" + id);
                return new User(id, $"user-{id}", id == 94 ? null : $"nick-{id}");
            }

            public int Touch()
            {
                s_calls.Enqueue("A:touch");
                return 1;
            }
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
            public async Task<User?> GetUserById(int id)
            {
                s_calls.Enqueue("B:" + id);
                await Task.Delay(s_delayB);

                return id switch
                {
                    98 => throw new GraphQLException("B hop failed for 98"),
                    97 or 93 => null,
                    _ => new User(id, new Info($"sku-{id}"), "eu")
                };
            }
        }
    }

    public static class CNested
    {
        public record Info([property: Shareable] string Sku);

        public record User([property: Shareable] Info Info, int? Total, int TotalNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public async Task<User?> GetUserBySku([Is("info.sku")] string sku)
            {
                s_calls.Enqueue("C:" + sku);
                await Task.Delay(s_delayC);

                if (sku == "sku-96")
                {
                    throw new GraphQLException("C hop failed for sku-96");
                }

                return new User(new Info(sku), int.Parse(sku[4..]), int.Parse(sku[4..]) * 100);
            }
        }
    }

    public static class CComposite
    {
        public record Info([property: Shareable] string Sku);

        public record User(
            [property: Shareable] Info Info,
            [property: Shareable] string Region,
            int? Total,
            int TotalNN);

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
            public async Task<User?> GetUserByKey([Is("{ sku: info.sku, region }")] KeyInput key)
            {
                s_calls.Enqueue("C:" + key.Sku + "/" + key.Region);
                await Task.Delay(s_delayC);
                return new User(new Info(key.Sku), key.Region, int.Parse(key.Sku[4..]), int.Parse(key.Sku[4..]) * 100);
            }
        }
    }

    public static class E
    {
        public record User(int Id, [property: Shareable] string? Code, User? Friend);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public async Task<User?> GetUserById(int id)
            {
                await Task.Delay(s_delayE);

                return id == 93
                    ? throw new GraphQLException("E hop failed for 93")
                    : new User(
                        id,
                        id == 98 ? null : $"code-{id}",
                        new User(id + 1000, id == 98 ? null : $"code-{id + 1000}", null));
            }
        }
    }

    public static class D
    {
        public record User([property: Shareable] string Code, [property: Shareable] int? Score, int ScoreNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public async Task<User?> GetUserByCode(string code)
            {
                await Task.Delay(s_delayD);
                return new User(code, code.Length, code.Length);
            }
        }
    }

    public static class F
    {
        public record User([property: Shareable] int? Score, int? Rank, int RankNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserByScore(int score) => new(score, score * 2, score * 3);
        }
    }

    public static class BFlat
    {
        public record User(int Id, string? Sku);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public UserObj? GetUserById(int id) => new(id);
        }

        [GraphQLName("User")]
        public class UserObj(int id)
        {
            public int Id => id;

            [Shareable]
            public string? Sku => id switch
            {
                98 => throw new GraphQLException("sku failed for 98"),
                94 => null,
                _ => $"sku-{id}"
            };
        }
    }

    public static class CFlat
    {
        public record User([property: Shareable] string Sku, int? Total, int TotalNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserBySku(string sku)
            {
                s_calls.Enqueue("C:" + sku);
                return new User(sku, int.Parse(sku[4..]), int.Parse(sku[4..]) * 100);
            }
        }
    }

    public static class H
    {
        public record User([property: Shareable] Info Info, int? Bonus, int BonusNN);

        public record Info([property: Shareable] string Sku);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public async Task<User?> GetUserBySku([Is("info.sku")] string sku)
            {
                await Task.Delay(s_delayH);
                return new User(new Info(sku), int.Parse(sku[4..]), int.Parse(sku[4..]) * 10);
            }
        }
    }

    public static class I
    {
        public record User(int Id, int? Bad, int BadNN);

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
                => id == 93
                    ? throw new GraphQLException("I hop failed for 93")
                    : new User(id, id, id);
        }
    }

    public static class J
    {
        public record User([property: Shareable] string Nick, int? NickRank, int NickRankNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserByNick(string nick)
                => new(nick, int.Parse(nick[5..]), int.Parse(nick[5..]) * 7);
        }
    }

    public static class BatchA
    {
        [EntityKey("id")]
        public record User(int Id, string Name);

        [EntityKey("id")]
        public record Account(int Id);

        public class Query
        {
            public IEnumerable<User?> GetNullableUsers(int[] ids) => ids.Select(i => new User(i, $"user-{i}"));

            public IEnumerable<User> GetUsers(int[] ids) => ids.Select(i => new User(i, $"user-{i}"));

            public IEnumerable<Account?> GetAccounts(int[] ids) => ids.Select(i => new Account(i));

            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, $"user-{id}");

            [Lookup, Internal]
            public Account? GetAccountById(int id) => new(id);
        }
    }

    public static class BatchE
    {
        public record Account(int Id, [property: Shareable] string Code);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public Account? GetAccountById(int id) => new(id, $"code-{id}");
        }
    }

    public static class BatchK
    {
        public record Info([property: Shareable] string Sku);

        public record User([property: Shareable] Info Info, int KayNN);

        public record Account([property: Shareable] string Code, int KayNN);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserBySku([Is("info.sku")] string sku) => new(new Info(sku), 2);

            [Lookup, Internal]
            public Account? GetAccountByCode(string code) => new(code, 3);
        }
    }

    public static class SplitUsers
    {
        [EntityKey("id")]
        public record User(int Id, string Name);

        public class Query
        {
            public IEnumerable<User?> GetNullableUsers(int[] ids) => ids.Select(i => new User(i, $"user-{i}"));

            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, $"user-{id}");
        }
    }

    public static class SplitAccounts
    {
        [EntityKey("id")]
        public record Account(int Id);

        public class Query
        {
            public async Task<IEnumerable<Account?>> GetAccounts(int[] ids)
            {
                await (ArrivalGate.Current?.BeforeAccountsAsync() ?? Task.CompletedTask);
                return ids.Select(i => new Account(i));
            }

            [Lookup, Internal]
            public Account? GetAccountById(int id) => new(id);
        }
    }
}
