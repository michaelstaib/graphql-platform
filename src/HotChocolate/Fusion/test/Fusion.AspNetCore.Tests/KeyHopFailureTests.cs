using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
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
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 98] plain
            data: {"users":[{"name":"user-2","total":2},{"name":"user-3","total":3},{"name":"user-98","total":null}]}
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 98] defer
            data: {"users":[{"name":"user-2"},{"name":"user-3"},{"name":"user-98"}]}
            incremental: [0]{"total":2} [1]{"total":3} [2]{"total":null}
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
    public async Task Execute_Should_ReportOneViolationAtTheFirstItem_When_MiddleHopFailsForNullableListOfNonNullItems()
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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["userList",0,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: 

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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["users",0,"totalNN"],"extensions":{"code":"HC0018"}}]

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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"totalNN"],"extensions":{"code":"HC0018"}},{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]

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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"kayNN"],"extensions":{"code":"HC0018"}},{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"kayNN"],"extensions":{"code":"HC0018"}}]

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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["users",0,"kayNN"],"extensions":{"code":"HC0018"}}]

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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"kayNN"],"extensions":{"code":"HC0018"}},{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"kayNN"],"extensions":{"code":"HC0018"}}]

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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 98, 3]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},null,{"name":"user-3","totalNN":300}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:98 C:sku-2 C:sku-3
            ## ids [2, 3, 98]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},{"name":"user-3","totalNN":300},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",2,"totalNN"],"extensions":{"code":"HC0018"}}]
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
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",0,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 98, 3]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},null,{"name":"user-3","totalNN":300}]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",1,"totalNN"],"extensions":{"code":"HC0018"}}]
            lookups: B:2 B:3 B:98 C:sku-2/eu C:sku-3/eu
            ## ids [2, 3, 98]
            data: {"nullableUsers":[{"name":"user-2","totalNN":200},{"name":"user-3","totalNN":300},null]}
            errors: [{"message":"Cannot return null for non-nullable field.","path":["nullableUsers",2,"totalNN"],"extensions":{"code":"HC0018"}}]
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
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? mockMiddleHop = null)
    {
        var a = CreateSourceSchema("a", b => b.AddQueryType<A.Query>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<B.Query>(), mockHttpResponse: mockMiddleHop);
        var c = CreateSourceSchema("c", b => b.AddQueryType<CNested.Query>());
        var d = CreateSourceSchema("d", b => b.AddQueryType<D.Query>());
        var e = CreateSourceSchema("e", b => b.AddQueryType<E.Query>());

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
            public async Task<User?> GetUserById(int id)
            {
                await Task.Delay(s_delayE);

                return id == 93
                    ? throw new GraphQLException("E hop failed for 93")
                    : new User(id, id == 98 ? null : $"code-{id}");
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
            public string? Sku => id == 94 ? null : $"sku-{id}";
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
