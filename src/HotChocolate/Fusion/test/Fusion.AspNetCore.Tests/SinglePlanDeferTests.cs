using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HotChocolate.Execution.Configuration;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

public partial class SinglePlanDeferTests : FusionTestBase
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_MatchThePlainTwin_When_TwoSwitchesSelectFieldsOfOtherSchemas(bool a, bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync(a && b ? ["B:5>", "C:4"] : []);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "price") { price }
                    ... @defer(if: $b, label: "reviews") { reviews { body } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_MatchThePlainTwin_When_PriceLivesOnTheParentSchema(bool a, bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            a && b ? ["A:4>", "C:3"] : [],
            ("A", """
                type Query { product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! price: Float! }
                """),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "price") { price }
                    ... @defer(if: $b, label: "reviews") { reviews { body } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_SendNameOnce_When_AFieldIsPlainUnderAConditionAndDeferred(bool x, bool d)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! @shareable }
                """),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! @shareable price: Float! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") { name price }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x, ["d"] = d });

        // assert
        MatchRun(run, $"x={x}, d={d}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_MatchThePlainTwin_When_SwitchesAreNested(bool a, bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync(a && b ? ["B:3>", "C:4"] : []);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "outer") {
                        price
                        ... @defer(if: $b, label: "inner") { reviews { body } }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Fact]
    public async Task Defer_Should_MatchThePlainTwin_When_TheAnchorIsInsideAList()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { products: [Product!]! product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! }
                """),
            ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name ... @defer(label: "price") { price } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_MatchThePlainTwin_When_DefersAreNestedWithoutASwitch()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync("B:2>", "C:3");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "outer") {
                        price
                        ... @defer(label: "inner") { reviews { body } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendASharedFieldOnce_When_SiblingGroupsSelectIt()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync("B:3>", "B:2");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    ... @defer(label: "a") { price rating }
                    ... @defer(label: "b") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheKeyInTheGroup_When_TheOnlyGroupSelectsTheKeyAndFieldsOfBothSchemas()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            NormalizeName,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    ... @defer { id name price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheFieldOnceWithTwoPendingIds_When_UnlabelledSiblingsSelectTheSameField()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { price }
                    ... @defer { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheFieldOnce_When_ALabelledAndAnUnlabelledSiblingSelectTheSameField()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync("B:2>", "B:3");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { price }
                    ... @defer(label: "labelled") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverBothNestedFragments_When_UnlabelledSiblingsShareTheOuterStep()
    {
        // arrange
        // the response of the shared outer lookup reaches the gateway first, then the shared nested lookup answers.
        var gateway = await CreateGatewayAsync(
            ["B:2>", "C:3"],
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { author { ... @defer { bio } } }
                    ... @defer { author { ... @defer { bio } } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
    }

    [Fact]
    public async Task Defer_Should_SendOneRequest_When_UnlabelledFragmentsSitAtAnAbstractAndAConcreteAnchor()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query {
                    items: [Item!]! @returns(types: ["Book", "Movie", "Book"])
                    book(id: ID!): Book @lookup
                }
                interface Item { id: ID! }
                type Book implements Item @key(fields: "id") { id: ID! }
                type Movie implements Item { id: ID! }
                """),
            ("B", """
                type Query { bookById(id: ID!): Book @lookup }
                type Book @key(fields: "id") { id: ID! rating: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                items { id ...F }
                book(id: "1") { id ...F }
            }

            fragment F on Book {
                ... @defer { rating }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
    }

    [Fact]
    public async Task Defer_Should_SendOneRequest_When_NestedUnlabelledFragmentsHaveDifferentParents()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { author { ... @defer { bio } } }
                    ... @defer { author { ... @defer { nick } } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_DeliverBothNestedFragments_When_AnotherSwitchedFragmentSelectsTheAuthor(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a) { author { ... @defer { bio } ... @defer { nick } } }
                    ... @defer(if: $b) { author { ... @defer { bio } } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_DeliverBothNestedFragments_When_AnotherSwitchedFragmentSelectsTheAuthorOfAList(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                products {
                    name
                    ... @defer(if: $a) { author { ... @defer { bio } ... @defer { nick } } }
                    ... @defer(if: $b) { author { ... @defer { bio } } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_DeliverBothNestedFragments_When_TheSwitchedFragmentsAreLabelled(bool a, bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            a && b ? ["B:8>", "B:6"] : [],
            ("A", ProductListSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "p") { author { ... @defer { bio } ... @defer { nick } } }
                    ... @defer(if: $b, label: "q") { author { ... @defer { bio } } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverBothNestedFragments_When_AnIncludedFieldSelectsTheAuthor(bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer { author { ... @defer { bio } ... @defer { nick } } }
                    author @include(if: $b) { id }
                }
            }
            """,
            new Dictionary<string, object?> { ["b"] = b });

        // assert
        MatchRun(run, $"b={b}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_NotSendTheKeyLookup_When_TheOperationThatTakesTheKeyIsNotNeeded(bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            b ? [] : ["B:7>", "D:4"],
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource),
            ("D", EditorKeySource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($b: Boolean!) {
                first: product(id: "1") {
                    name
                    ... @defer { author { ... @defer { bio } } }
                    author @include(if: $b) { id }
                }
                second: product(id: "2") {
                    name
                    ... @defer { editor { ... @defer { nick } } }
                    editor @include(if: $b) { nick }
                }
            }
            """,
            new Dictionary<string, object?> { ["b"] = b });

        // assert
        MatchRun(run, $"b={b}");
        Assert.Equal(1, run.Deferred.Requests["D"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheMergedLookupOfTwoTargets_When_ItTakesItsKeysFromOneStep(bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", AuthorAndEditorKeySource),
            ("C", AuthorBioSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer { author { bio } editor { bio } }
                    author @include(if: $b) { id }
                    editor @include(if: $b) { id }
                }
            }
            """,
            new Dictionary<string, object?> { ["b"] = b });

        // assert
        MatchRun(run, $"b={b}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_SendOneRequestPerSchema_When_MergedLookupsOfSwitchedFragmentsShareABatch(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { product(id: ID!): Product @lookup topProduct: Product }
                type Product @key(fields: "id") { id: ID! name: String! }
                """),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer {
                        ... @include(if: $a) { author { ... @defer { bio } } }
                        ... @include(if: $b) { author { ... @defer { nick } } }
                    }
                }
                topProduct {
                    name
                    ... @defer { author { id } }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheDependentField_When_TheOperationThatSuppliesItsKeyDeliversToAnotherGroup(
        bool c)
    {
        // arrange
        var gateway = await CreateSkuChainGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($c: Boolean!) {
                current {
                    user {
                        ... @defer(if: $c) { sku }
                        ... @defer { sku total }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["c"] = c });

        // assert
        MatchRun(run, $"c={c}", compareValues: false);
        Assert.Equal(1, run.Deferred.Requests["B"]);
    }

    [Fact]
    public async Task Defer_Should_FetchTheDependentField_When_OnlyTheInnerFragmentOfTheSecondOuterFragmentIsOn()
    {
        // arrange
        var gateway = await CreateSkuChainGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!, $c: Boolean!, $d: Boolean!) {
                current {
                    user {
                        ... @defer(if: $a) { friend { code ... @defer(if: $c) { sku } } }
                        ... @defer(if: $b) { friend { code ... @defer(if: $d) { sku total } } }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = false, ["b"] = false, ["c"] = false, ["d"] = true });

        // assert
        MatchRun(run, compareValues: false);
        Assert.Equal(1, run.Deferred.Requests["B"]);
    }

    private static readonly (string Name, string Sdl)[] s_skuChainSources =
    [
        ("A", """
            type Query { current: Holder! userById(id: ID!): User @lookup @internal }
            type Holder { user: User! }
            type User @key(fields: "id") { id: ID! friend: User! }
            """),
        ("B", """
            type Query { userBySku(sku: String!): User @lookup @internal }
            type User @key(fields: "sku") { sku: String! @shareable total: Int! }
            """),
        ("C", """
            type Query { userById(id: ID!): User @lookup @internal }
            type User @key(fields: "id") { id: ID! code: String! @shareable }
            """),
        ("D", """
            type Query { userByCode(code: String!): User @lookup @internal }
            type User @key(fields: "code") { code: String! @shareable sku: String! @shareable }
            """)
    ];

    private Task<Gateway> CreateSkuChainGatewayAsync()
        => CreateGatewayAsync(s_skuChainSources);

    [Fact]
    public async Task Defer_Should_SendOuterDataBeforeInnerData_When_TheLookupsOfAnOuterAndAnInnerGroupShareARequest()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { products: [Product!]! productByUpc(upc: String!): Product @lookup }
                type Product @key(fields: "upc") {
                    upc: String!
                    eur(price: Int @require(field: "price(cur: \"EUR\")")): Int
                    usd(price: Int @require(field: "price(cur: \"USD\")")): Int
                }
                """),
            ("B", """
                type Query { productByUpcB(upc: String!): Product @lookup }
                type Product @key(fields: "upc") { upc: String! price(cur: String): Int }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { upc ... @defer { eur ... @defer { usd } } } }
            """,
            []);

        // assert
        MatchRun(run, compareValues: false);
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_AnnounceTheGroupThatHoldsTheField_When_TheInnerSwitchIsConditional(bool c)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($c: Boolean!) {
                product(id: "1") {
                    ... @defer(label: "o") {
                        ... @defer(if: $c, label: "c") { price }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["c"] = c });

        // assert
        MatchRun(run, $"c={c}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition(bool x)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { user(id: ID!): User @lookup }
                type User @key(fields: "id") { id: ID! friends: [User!]! }
                """),
            ("E", """
                type Query { userById(id: ID!): User @lookup }
                type User @key(fields: "id") { id: ID! name: String! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                user(id: "1") {
                    friends @include(if: $x) { id }
                    ... @defer(label: "o") { friends { ... @defer(label: "c") { name } } }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Defer_Should_SkipTheLookupOfTheSwitchThatIsOn_When_SiblingSwitchesSelectTheSameFields(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync(a && b ? ["B:3>", "B:4"] : []);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "a") { price }
                    ... @defer(if: $b, label: "b") { price }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Fact]
    public async Task Defer_Should_SendTheSharedFieldOnlyInTheInitialPayload_When_OnlyTheOtherSwitchIsOn()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "a") { price rating }
                    ... @defer(if: $b, label: "b") { price }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = true, ["b"] = false });

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_StartTheNestedLookupBeforeTheOuterLookupAnswers_When_TheKeyIsKnown()
    {
        // arrange
        // the response for the price is held back until the step that fetches the rating is done.
        var gateway = await CreateGatewayAsync(
            ["C:3", "B:2"],
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! }
                """),
            ("C", """
                type Query { productByIdForRating(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! rating: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        price
                        ... @defer(label: "i") { rating }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.True(run.Deferred.IndexOfPending("i") >= run.Deferred.IndexOfCompleted("o"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Defer_Should_DeliverOnlyTheFieldsLeft_When_AFieldIsSelectedInAnInitialAndADeferredSwitch(
        bool a,
        bool b)
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "a") { price rating }
                    ... @defer(if: $b, label: "b") { price }
                }
            }
            """,
            new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

        // assert
        MatchRun(run, $"a={a}, b={b}");
    }

    [Fact]
    public async Task Defer_Should_SendTypenameInTheInitialPayload_When_TheGroupSelectsOnlyTypename()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "t") { __typename } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheSharedNestedFieldWithTheLongestPath_When_TheTopGroupComesBeforeTheOuterGroup(
        bool topStepFirst)
    {
        // arrange
        var gateway = await CreateProductReviewGatewayAsync(
            topStepFirst ? ["B:2", "C:3>", "B:4>", "C:5"] : ["B:4", "C:5", "B:2@2", "C:3@3"]);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                ... @defer(label: "top") { products { reviews { rating } } }
                products {
                    id
                    ... @defer(label: "outer") {
                        reviews { id ... @defer(label: "inner") { rating } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"topStepFirst={topStepFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheSharedNestedFieldWithTheLongestPath_When_TheTopGroupComesAfterTheOuterGroup(
        bool topStepFirst)
    {
        // arrange
        var gateway = await CreateProductReviewGatewayAsync(
            topStepFirst ? ["B:2", "C:3>", "B:4>", "C:5"] : ["B:4", "C:5", "B:2@2", "C:3@3"]);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products {
                    id
                    ... @defer(label: "outer") {
                        reviews { id ... @defer(label: "inner") { rating } }
                    }
                }
                ... @defer(label: "top") { products { reviews { rating } } }
            }
            """,
            []);

        // assert
        MatchRun(run, $"topStepFirst={topStepFirst}");
    }

    [Fact]
    public async Task Defer_Should_EndWithTheLastPayload_When_TheStepOfADroppedFragmentFailsAfterIt()
    {
        // arrange
        // the rating goes out under top, so inner is dropped and its step fails after the last payload
        var gateway = await CreateProductReviewGatewayAsync("B:2", "C:3>", "B:4>", "C:5");
        _gate!.FailingStep = "C:5";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                ... @defer(label: "top") { products { reviews { rating } } }
                products {
                    id
                    ... @defer(label: "outer") {
                        reviews { id ... @defer(label: "inner") { rating } }
                    }
                }
            }
            """,
            []);

        // assert
        Assert.Equal(3, run.Payloads.Count);
        MatchPayloadShapes(run);
    }

    [Fact]
    public async Task Defer_Should_CancelTheStepOfADroppedFragment_When_TheSourceNeverAnswersAfterTheLastPayload()
    {
        // arrange
        // the rating goes out under top, so inner is dropped and the source never answers its step
        var gateway = await CreateGatewayAsync(
            ["B:2", "C:3>", "B:4>", "C:5"],
            rewrite: null,
            failingSchema: null,
            stalledSchema: "C",
            configure: null,
            s_productReviewSources);
        _gate!.StalledStep = "C:5";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                ... @defer(label: "top") { products { reviews { rating } } }
                products {
                    id
                    ... @defer(label: "outer") {
                        reviews { id ... @defer(label: "inner") { rating } }
                    }
                }
            }
            """,
            []).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(["C:5"], _gate.CancelledSteps);
        MatchPayloadShapes(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_AnnounceTheNestedFragment_When_ItsStepAnswersNothingAfterTheOuterOneSendsTheAuthor(
        bool nullableBio)
    {
        // arrange
        // the source of the bio answers every lookup with null
        var gateway = await CreateGatewayAsync(
            ["B:2>", "C:3"],
            rewrite: (schema, _, text) => schema == "C"
                ? "{\"variableIndex\":0,\"data\":{\"authorById\":null}}\n"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", nullableBio ? AuthorNullableBioSource : AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id ... @defer(label: "c") { bio } } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"nullableBio={nullableBio}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheRequiredFieldOfAThirdSchema_When_TheSwitchIsOn(bool s)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! weight: Float! }
                """),
            ("C", """
                type Query { productByIdForShipping(id: ID!): Product @lookup }
                type Product @key(fields: "id") {
                    id: ID!
                    shipping(weight: Float! @require(field: "weight")): Float!
                }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($s: Boolean!) {
                product(id: "1") { name ... @defer(if: $s, label: "shipping") { shipping } }
            }
            """,
            new Dictionary<string, object?> { ["s"] = s });

        // assert
        MatchRun(run, $"s={s}");
    }

    [Fact]
    public async Task Defer_Should_ReturnTheBytesOfThePlainPath_When_TheOperationHasNoDefer()
    {
        // arrange
        var sources = new[]
        {
            ("A", ProductSource),
            ("B", PriceSource)
        };
        var withOption = await CreateGatewayAsync(sources);
        var withoutOption = await CreateCompositeSchemaAsync(
            [.. sources.Select(t => (t.Item1, CreateSourceSchema(t.Item1, t.Item2)))]);

        // act
        var onBody = await ReadBodyAsync(withOption, "{ product(id: \"1\") { name price rating } }");
        var offBody = await ReadBodyAsync(withoutOption, "{ product(id: \"1\") { name price rating } }");

        // assert
        Assert.Equal(offBody, onBody);
    }

    [Fact]
    public async Task Defer_Should_KeepTheFirstResult_When_TheSecondStepReturnsAValueAfterAnError()
    {
        // arrange
        var gateway = await CreateValueAndErrorGatewayAsync("C:2", "B:3");

        // act
        var run = await RunDeferredAsync(gateway, ValueAndErrorQuery, []);

        // assert
        Assert.Empty(run.Problems);
        Canonical(run.Data).MatchInlineSnapshot(
            """{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","price":null,"rating":123,"weight":123}}""");
        run.Errors.MatchInlineSnapshot(
            """{"message":"Unexpected Execution Error","path":["product","price"]}""");
    }

    [Fact]
    public async Task Defer_Should_KeepTheFirstResult_When_TheSecondStepReturnsAnErrorAfterAValue()
    {
        // arrange
        var gateway = await CreateValueAndErrorGatewayAsync("B:3", "C:2");

        // act
        var run = await RunDeferredAsync(gateway, ValueAndErrorQuery, []);

        // assert
        Assert.Empty(run.Problems);
        Canonical(run.Data).MatchInlineSnapshot(
            """{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","price":123.456,"rating":123,"weight":123}}""");
        Assert.Empty(run.Errors);
    }

    private const string ValueAndErrorQuery =
        """
        {
            product(id: "1") {
                name
                ... @defer(label: "a") { price rating }
                ... @defer(label: "b") { price weight }
            }
        }
        """;

    // The price comes from two sources, one answers with a value and the other with an error.
    private Task<Gateway> CreateValueAndErrorGatewayAsync(string first, string second)
        => CreateGatewayAsync(
            [first, second],
            ("A", ProductListSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float @shareable rating: Int! }
                """),
            ("C", """
                type Query { productByIdForWeight(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float @shareable @error weight: Int! }
                """));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheLeavesOfBothGroups_When_SiblingGroupsSelectDifferentLeavesOfTheSameList(
        bool idGroupFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            idGroupFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            ("A", ProductListSource),
            ("B", ReviewBodySource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { reviews { id } }
                    ... @defer(label: "b") { reviews { body } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"idGroupFirst={idGroupFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheDependentField_When_TheKeyListIsSelectedByASiblingGroup(bool keyGroupFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            keyGroupFirst ? ["B:4>", "B:2", "C:3"] : ["B:2", "C:3>", "B:4"],
            ("A", ProductListSource),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { reviews { id } }
                    ... @defer(label: "b") { reviews { rating } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"keyGroupFirst={keyGroupFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheDependentField_When_TheKeyObjectIsSelectedByASiblingGroup(
        bool keyGroupFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            keyGroupFirst ? ["B:4>", "B:2", "C:3"] : ["B:2", "C:3>", "B:4"],
            ("A", ProductListSource),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { topReview { id } }
                    ... @defer(label: "b") { topReview { rating } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"keyGroupFirst={keyGroupFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheRequiredField_When_TheRequirementIsSelectedByASiblingGroup(
        bool requirementGroupFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            requirementGroupFirst ? ["B:5>", "B:4", "C:3"] : ["B:4", "C:3>", "B:5"],
            ("A", ProductListSource),
            ("B", WeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { weight }
                    ... @defer(label: "b") { shipping }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"requirementGroupFirst={requirementGroupFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_RunTheDependentStep_When_TheRequirementPathIsDeeperThanEightLevels(
        bool requirementStepFirst)
    {
        // arrange
        const int depth = 12;
        var requirementPath = string.Concat(Enumerable.Repeat("n.", depth)) + "w";
        var chain = string.Concat(Enumerable.Repeat("n { ", depth))
            + "w"
            + string.Concat(Enumerable.Repeat(" }", depth));
        var types = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, depth).Select(i => i == depth
                ? $"type N{i} {{ w: Float! }}"
                : $"type N{i} {{ n: N{i + 1}! }}"));
        var gateway = await CreateGatewayAsync(
            requirementStepFirst ? ["B:4", "C:3>", "B:5"] : ["B:5>", "B:4", "C:3"],
            ("A", ProductListSource),
            ("B", $$"""
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! n: N1! }
                {{types}}
                """),
            ("C", $$"""
                type Query { productByIdForShipping(id: ID!): Product @lookup }
                type Product @key(fields: "id") {
                    id: ID!
                    shipping(weight: Float! @require(field: "{{requirementPath}}")): Float!
                }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            $$"""
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { {{chain}} }
                    ... @defer(label: "b") { shipping }
                }
            }
            """,
            []);

        // assert
        Assert.Equal(1, run.Deferred.Requests["C"]);
        MatchRun(run, $"requirementStepFirst={requirementStepFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheRequiredField_When_TheRequirementIsSelectedPlainUnderACondition(bool x)
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", WeightSource), ("C", ShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                product(id: "1") {
                    name
                    weight @include(if: $x)
                    ... @defer(label: "b") { shipping }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheDependentField_When_TheKeyIsSelectedPlainUnderACondition(bool x)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "b") { reviews { id @include(if: $x) rating } }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheFieldsOfTheFragment_When_AConditionDecidesWhetherItsOnlyStepRuns(bool c)
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($c: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "d") { id rating @include(if: $c) }
                }
            }
            """,
            new Dictionary<string, object?> { ["c"] = c });

        // assert
        MatchRun(run, $"c={c}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheFieldsOfEveryRow_When_ASkipDecidesWhetherTheOnlyStepRuns(bool s)
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($s: Boolean!) {
                products {
                    name
                    ... @defer(label: "d") { id rating @skip(if: $s) }
                }
            }
            """,
            new Dictionary<string, object?> { ["s"] = s });

        // assert
        MatchRun(run, $"s={s}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverBothFragments_When_AConditionDecidesWhetherTheOnlyStepOfTheOuterRuns(bool c)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            c ? ["B:2>", "B:3"] : [],
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($c: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        id
                        rating @include(if: $c)
                        ... @defer(label: "i") { price }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["c"] = c });

        // assert
        MatchRun(run, $"c={c}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheFieldsOfTheFragment_When_NoStepOfItsOwnFetchesThem(bool d)
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($d: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        id
                        ... @defer(label: "i", if: $d) { rating }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["d"] = d });

        // assert
        MatchRun(run, $"d={d}");
    }

    [Fact]
    public async Task Defer_Should_SendASinglePlainResult_When_AFalseConditionSwitchesOffEveryFieldOfTheFragment()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($c: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "d") { rating @include(if: $c) }
                }
            }
            """,
            new Dictionary<string, object?> { ["c"] = false });

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheValueOfItsOwnStep_When_TheChildStepFetchesTheSameFieldFirst(
        bool childFirst)
    {
        // arrange
        // the step of the inner group answers the price with 777, the step of the outer group does not.
        var gateway = await CreateGatewayAsync(
            childFirst ? ["B:3", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) => schema == "B" && body.Contains("rating")
                ? PriceValue().Replace(text, "\"price\":777")
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            query($f: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        price @include(if: $f)
                        ... @defer(label: "i") { price rating }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["f"] = true });

        // assert
        MatchPayloads(run, $"childFirst={childFirst}");
    }

    [GeneratedRegex("\"price\":[^,}]*")]
    private static partial Regex PriceValue();

    [Fact]
    public async Task Defer_Should_DeliverTheDeferredLeaf_When_ItsListIsAlsoSelectedPlain()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("C", ReviewStarsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    reviews { body }
                    ... @defer(label: "d") { reviews { stars } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheDeferredLeaf_When_ItsObjectsAreAlsoSelectedPlain()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", ReviewBodySource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(label: "d") { reviews { body } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FetchTheDeferredLeafThroughTheLookupOfTheItem_When_ItsListIsAlsoSelectedPlain()
    {
        // arrange
        var listRequests = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                if (schema == "C" && body.Contains("productByIdForReviews"))
                {
                    Interlocked.Increment(ref listRequests);
                }

                return text;
            },
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("C", """
                type Query {
                    productByIdForReviews(id: ID!): Product @lookup
                    reviewById(id: ID!): Review @lookup
                }
                type Product @key(fields: "id") { id: ID! reviews: [Review!]! }
                type Review @key(fields: "id") { id: ID! body: String! stars: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    reviews { body }
                    ... @defer(label: "d") { reviews { stars } }
                }
            }
            """,
            []);

        // assert
        // The deferred run and its plain twin fetch the list once each.
        Assert.Equal(2, listRequests);
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_AnnounceTheNestedGroupWhenItsParentCompletes_When_TheNestedStepAnswersFirst()
    {
        // arrange
        var gateway = await CreateGatewayAsync(["B:3", "B:2"], ("A", ProductListSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products { name }
                ... @defer(label: "outer") {
                    products {
                        price
                        ... @defer(label: "inner") { rating }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FetchTheListOnce_When_ATypedFragmentSelectsItBelowAListOfTheInitialPayload()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { product(id: ID!): Product @lookup search: [SearchResult!]! }
                union SearchResult = Product | Article
                type Product @key(fields: "id") { id: ID! name: String! }
                type Article @key(fields: "id") { id: ID! title: String! }
                """),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                search { ... on Product { name reviews { id } } }
                ... @defer(label: "d") { search { ... on Product { reviews { rating } } } }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_TheObjectAboveItIsSelectedOutsideUnderACondition(bool x)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                product(id: "1") @include(if: $x) { name reviews { id } }
                ... @defer(label: "d") { product(id: "1") { reviews { rating } } }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        MatchRun(run, $"x={x}");
    }

    [Fact]
    public async Task Defer_Should_AnnounceTheGroupOfAPartOnceAtItsPlace_When_ACopyOfANestedSwitchIsRemoved()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:5>", "B:2>", "B:3"],
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($v: Boolean!) {
                products { name }
                product(id: "1") {
                    name
                    ... @defer(if: $v, label: "outer") {
                        price
                        ... @defer(if: $v, label: "inner") { rating }
                    }
                }
                ... @defer(label: "top") { products { price } }
            }
            """,
            new Dictionary<string, object?> { ["v"] = true });

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FetchTheListOnce_When_ANestedFragmentSelectsAListOfTheOutsideSelection()
    {
        // arrange
        var listRequests = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                if (schema == "B" && body.Contains("reviews"))
                {
                    Interlocked.Increment(ref listRequests);
                }

                return text;
            },
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! reviews: [Review!]! }
                type Review @key(fields: "id") { id: ID! @shareable }
                """),
            ("C", """
                type Query { reviewById(id: ID!): Review @lookup }
                type Review @key(fields: "id") { id: ID! stars: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products { name reviews { id } }
                ... @defer(label: "o") {
                    products {
                        price
                        ... @defer(label: "i") { reviews { stars } }
                    }
                }
            }
            """,
            []);

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests);
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_ANestedFragmentSelectsAListOfAConditionalOutsideSelection(
        bool x)
    {
        // arrange
        var listRequests = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                if (schema == "B" && body.Contains("reviews"))
                {
                    Interlocked.Increment(ref listRequests);
                }

                return text;
            },
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! reviews: [Review!]! }
                type Review @key(fields: "id") { id: ID! @shareable }
                """),
            ("C", """
                type Query { reviewById(id: ID!): Review @lookup }
                type Review @key(fields: "id") { id: ID! stars: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                products @include(if: $x) { name reviews { id } }
                ... @defer(label: "o") {
                    products {
                        price
                        ... @defer(label: "i") { reviews { stars } }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests);
        MatchRun(run, $"x={x}");
    }

    [Fact]
    public async Task Defer_Should_DeliverTheNestedFragmentSeparately_When_AnotherNodeSelectsTheParentFieldWithoutIt()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ShelfTopSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                shelf { id }
                ... @defer(label: "outer") {
                    shelf {
                        top {
                            ... @defer(label: "inner") { price }
                        }
                    }
                }
            }
            """,
            []);

        // assert
        Assert.Equal(2, run.Deferred.PendingCount);
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_AnObjectLiesBetweenAConditionalFieldAndANestedFragment(
        bool x)
    {
        // arrange
        var listRequests = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                if (schema == "B" && body.Contains("reviews"))
                {
                    Interlocked.Increment(ref listRequests);
                }

                return text;
            },
            failingSchema: null,
            configure: null,
            ("A", ShelfSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! reviews: [Review!]! }
                type Review @key(fields: "id") { id: ID! @shareable }
                """),
            ("C", """
                type Query { reviewById(id: ID!): Review @lookup }
                type Review @key(fields: "id") { id: ID! stars: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                shelf @include(if: $x) { items { name reviews { id } } }
                ... @defer(label: "o") {
                    shelf {
                        items {
                            price
                            ... @defer(label: "i") { reviews { stars } }
                        }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests);
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_AFragmentOnlyWrapsANestedFragmentThatSelectsTheList(bool x)
    {
        // arrange
        var listRequests = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                if (schema == "B" && body.Contains("reviews"))
                {
                    Interlocked.Increment(ref listRequests);
                }

                return text;
            },
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! reviews: [Review!]! }
                type Review @key(fields: "id") { id: ID! @shareable }
                """),
            ("C", """
                type Query { reviewById(id: ID!): Review @lookup }
                type Review @key(fields: "id") { id: ID! stars: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                products @include(if: $x) { name reviews { id } }
                ... @defer(label: "o") {
                    products {
                        price
                        ... @defer(label: "m") {
                            ... @defer(label: "i") { reviews { stars } }
                        }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests);
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_AnObjectBelowAConditionalFieldHoldsTheNestedFragment(bool x)
    {
        // arrange
        var (gateway, listRequests) = await CreateReviewListGatewayAsync(ShelfTopSource);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                shelf @include(if: $x) { top { name reviews { id } } }
                ... @defer(label: "o") {
                    shelf {
                        top {
                            price
                            ... @defer(label: "i") { reviews { stars } }
                        }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests());
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_AConditionalFieldOfARootFieldHoldsTheNestedFragment(bool x)
    {
        // arrange
        var (gateway, listRequests) = await CreateReviewListGatewayAsync(ShelfTopSource);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                product(id: "1") @include(if: $x) { related { name reviews { id } } }
                ... @defer(label: "o") {
                    product(id: "1") {
                        related {
                            price
                            ... @defer(label: "i") { reviews { stars } }
                        }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests());
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_TheConditionalFieldIsBelowAFieldThatIsAlwaysSelected(bool x)
    {
        // arrange
        var (gateway, listRequests) = await CreateReviewListGatewayAsync(ShelfTopSource);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                shelf { id top @include(if: $x) { name reviews { id } } }
                ... @defer(label: "o") {
                    shelf {
                        top {
                            price
                            ... @defer(label: "i") { reviews { stars } }
                        }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests());
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
        MatchRun(run, $"x={x}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FetchTheListOnce_When_AFragmentWrapsTheNestedFragmentBelowAnObject(bool x)
    {
        // arrange
        var (gateway, listRequests) = await CreateReviewListGatewayAsync(ShelfSource);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($x: Boolean!) {
                shelf @include(if: $x) { items { name reviews { id } } }
                ... @defer(label: "o") {
                    shelf {
                        items { price }
                        ... @defer(label: "m") {
                            items {
                                ... @defer(label: "i") { reviews { stars } }
                            }
                        }
                    }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = x });

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests());
        Assert.Equal(run.Twin.RequestSummary, run.Deferred.RequestSummary);
        MatchRun(run, $"x={x}");
    }

    [Fact]
    public async Task Defer_Should_FetchTheListOnce_When_AFragmentWrapsTheNestedFragmentBelowAnObjectWithoutACondition()
    {
        // arrange
        var (gateway, listRequests) = await CreateReviewListGatewayAsync(ShelfSource);

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                shelf { items { name reviews { id } } }
                ... @defer(label: "o") {
                    shelf {
                        items { price }
                        ... @defer(label: "m") {
                            items {
                                ... @defer(label: "i") { reviews { stars } }
                            }
                        }
                    }
                }
            }
            """,
            []);

        // assert
        // The deferred run and its plain twin ask for the list once each.
        Assert.Equal(2, listRequests());
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FetchTheReviewsOnceAndTheRatingOncePerGroup_When_TwoGroupsSelectTheSameList()
    {
        // arrange
        var gateway = await CreateProductReviewGatewayAsync("C:3>", "C:4");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products {
                    id
                    ... @defer(label: "o") { reviews { id rating } }
                    reviews { id ... @defer(label: "i") { rating } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_JoinThePriceToItsItem_When_AnotherFetchOfTheListWouldReturnTheItemsInAnotherOrder()
    {
        // arrange
        var listRequests = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                // A second fetch of the list answers with the items in the reverse order.
                if (schema == "A" && body.Contains("products"))
                {
                    string[] ids = Interlocked.Increment(ref listRequests) == 1
                        ? ["p0", "p1", "p2"]
                        : ["p2", "p1", "p0"];
                    var items = string.Join(",", ids.Select(id => $"{{\"name\":\"n-{id}\",\"id\":\"{id}\"}}"));

                    return $"{{\"data\":{{\"products\":[{items}]}}}}";
                }

                // The price of an item is 10 plus the number in its id.
                return schema == "B"
                    ? string.Concat(
                        ItemKey().Matches(body).Select((m, i) =>
                            $"{{\"variableIndex\":{i},\"data\":{{\"productById\":"
                            + $"{{\"price\":{10 + int.Parse(m.Groups[1].Value)}}}}}}}\n"))
                    : text;
            },
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                products { name }
                ... @defer(label: "top") { products { price } }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [GeneratedRegex("\"__fusion_\\d+_id\":\"p(\\d)\"")]
    private static partial Regex ItemKey();

    [Fact]
    public async Task Defer_Should_DeliverPriceForEveryItem_When_ARootGroupSelectsAListOfTheInitialPayload()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products { name }
                ... @defer(label: "top") { products { price } }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_NotSendTheListAgain_When_TheGroupAddsFieldsToAListOfTheInitialPayload()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(label: "b") { reviews { rating } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResult_When_TheRootIsNulledInTheInitialPhase()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { products: [Product!]! @error product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! }
                """),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteThePendingId_When_TheMarkedStepFailsInTransport()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReportTheError_When_TheErrorNullsEveryRowOfTheGroup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductListSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! @error rating: Int! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteEveryAnnouncedId_When_TheLoopFaults()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3>", "C:2"],
            rewrite: null,
            failingSchema: "C",
            configure: builder => builder.AddErrorFilter(error => error.Path?.ToString().Contains("reviews") is true
                ? throw new InvalidOperationException("The error filter faulted.")
                : error),
            ("A", ProductSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { price }
                    ... @defer(label: "b") { reviews { body } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_NotReportAnErrorForADeliveredField_When_TheMarkedStepFailsInTransport()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: null,
            ("A", """
                type Query { product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! @shareable }
                """),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! @shareable price: Float! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") { name price }
                }
            }
            """,
            new Dictionary<string, object?> { ["x"] = true, ["d"] = true });

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheDeferredField_When_TheOperationIsAMutation()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", MutationSource), ("B", PriceSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            mutation { createProduct { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_RunTheRootFieldsOneAfterTheOther_When_TheMutationHasTwoRootFields()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", MutationSource), ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            mutation {
                first: createProduct { name ... @defer(label: "p") { price } }
                second: createProduct { name }
            }
            """,
            []);

        // assert
        MatchPayloadShapes(run);
        Assert.Equal(
            ["enter A:1", "done A:1", "enter A:3", "done A:3"],
            _gate!.Events.Where(t => t.Contains(" A:")).ToArray());
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResult_When_TheOperationHasMoreDeliveryGroupsThanTheDocumentCanMark()
    {
        // arrange
        var gateway = await CreateProductGatewayAsync();
        var fragments = string.Concat(Enumerable.Repeat("... @defer { price } ", 1024));

        // act
        var run = await RunDeferredAsync(
            gateway,
            "{ product(id: \"1\") { name " + fragments + "} }",
            []);

        // assert
        MatchPayloadShapes(run);
    }

    [Fact]
    public async Task Defer_Should_RefuseBeforeAnySourceRequest_When_TheClientAcceptsNoStreamFormat()
    {
        // arrange
        var sources = new[] { ("A", ProductSource), ("B", PriceSource) };
        var withOption = await CreateGatewayAsync(sources);
        var withoutOption = await CreateCompositeSchemaAsync(
            [.. sources.Select(t => (t.Item1, CreateSourceSchema(t.Item1, t.Item2)))]);
        const string body = """{"query":"{ product(id: \"1\") { name ... @defer(label: \"p\") { price } } }"}""";

        // act
        var onResponse = await PostAsync(withOption, body, "application/graphql-response+json");
        var offResponse = await PostAsync(withoutOption, body, "application/graphql-response+json");

        // assert
        Assert.Equal(offResponse, onResponse);
        Assert.Equal(0, CountRequests(withOption).Values.Sum());
    }

    [Fact]
    public async Task Defer_Should_RejectTheRequest_When_VariablesAreBatched()
    {
        // arrange
        var sources = new[] { ("A", ProductSource), ("B", PriceSource) };
        var withOption = await CreateGatewayAsync(sources);
        var withoutOption = await CreateCompositeSchemaAsync(
            [.. sources.Select(t => (t.Item1, CreateSourceSchema(t.Item1, t.Item2)))]);
        const string body =
            """
            {
                "query": "query($a: Boolean!) { product(id: \"1\") { ... @defer(if: $a, label: \"p\") { price } } }",
                "variables": [{ "a": true }, { "a": false }]
            }
            """;

        // act
        var onResponse = await PostAsync(withOption, body, "application/graphql-response+json, application/jsonl");
        var offResponse = await PostAsync(withoutOption, body, "application/graphql-response+json, application/jsonl");

        // assert
        Assert.Equal(offResponse, onResponse);
        Assert.Equal(0, CountRequests(withOption).Values.Sum());
        Assert.StartsWith("400", onResponse);
    }

    [Fact]
    public async Task Defer_Should_SendTheKey_When_TheGroupSelectsTheKeyOfItsLookup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { id price } } }
            """,
            []);

        // assert
        Assert.Equal(run.Twin.Requests, run.Deferred.Requests);
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheKeyInTheGroupOfEveryItem_When_TheLookupOfTheGroupUsesAnotherKey()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            NormalizeName,
            failingSchema: null,
            configure: null,
            ("A", ProductTwoKeysSource),
            ("B", ProductBySkuSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name ... @defer(label: "d") { id price } } }
            """,
            []);

        // assert
        Assert.Equal(run.Twin.Requests, run.Deferred.Requests);
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheKeyInBothGroups_When_TheLookupOfOneGroupUsesAnotherKey(bool priceGroupFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            priceGroupFirst ? ["B:3>", "A:2"] : ["A:2>", "B:3"],
            NormalizeName,
            failingSchema: null,
            configure: null,
            ("A", ProductTwoKeysSource),
            ("B", ProductBySkuSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products {
                    name
                    ... @defer(label: "d") { id price }
                    ... @defer(label: "e") { id }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"priceGroupFirst={priceGroupFirst}");
    }

    [Fact]
    public async Task Defer_Should_SendTheKeyOfEveryItem_When_TheGroupSelectsTheKeyOfItsLookup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name ... @defer(label: "d") { id price } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheKey_When_AnotherGroupFetchesThroughIt(bool keyStepFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            keyStepFirst ? ["A:3", "B:2"] : ["B:2>", "A:3"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "d") { id }
                    ... @defer(label: "e") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"keyStepFirst={keyStepFirst}");
    }

    [Fact]
    public async Task Defer_Should_SendTheRequirement_When_AStepWithoutAGroupFetchesIt()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["C:2"],
            ("A", ProductWeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "a") { weight } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheRequirement_When_ASiblingGroupDependsOnIt(bool dependentFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            dependentFirst ? ["C:2", "A:3"] : ["A:3", "C:2"],
            ("A", ProductWeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { weight }
                    ... @defer(label: "b") { shipping }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"dependentFirst={dependentFirst}");
    }

    [Fact]
    public async Task Defer_Should_SendTypename_When_TheGroupSelectsItNextToAField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { __typename price } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTypename_When_AMarkedStepCreatesTheObject()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2", "C:3"],
            ("A", ProductListSource),
            ("B", ReviewKeySource),
            ("C", ReviewRatingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        reviews { ... @defer(label: "i") { __typename id rating } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheKey_When_TheGroupHasASwitch(bool d)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            d ? ["B:2"] : [],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($d: Boolean!) {
                product(id: "1") { name ... @defer(if: $d, label: "d") { id price } }
            }
            """,
            new Dictionary<string, object?> { ["d"] = d });

        // assert
        MatchRun(run, $"d={d}");
    }

    [Fact]
    public async Task Defer_Should_SendEveryItem_When_OneSourceObjectHasSeveralTargets()
    {
        // arrange
        // source A answers every product with the same id, so the objects of the group share one source object.
        var gateway = await CreateGatewayAsync(
            ["B:2"],
            (name, _, text) => name == "A"
                ? EntityId().Replace(text, "\"id\":\"UHJvZHVjdDox\"")
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name price } again: products { name ... @defer(label: "d") { price } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheKey_When_TheGroupIsNested(bool innerFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            innerFirst ? ["B:3", "B:2"] : ["B:2>", "B:3"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        price
                        ... @defer(label: "i") { id rating }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run, $"innerFirst={innerFirst}");
    }

    [GeneratedRegex("\"id\":\"[^\"]*\"")]
    private static partial Regex EntityId();

    private const string ProductWeightSource =
        """
        type Query { product(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! name: String! weight: Float! }
        """;

    [Fact]
    public async Task Defer_Should_SendIntrospectionWithTheInitialPayload_When_ARootGroupSelectsOnlyIntrospection()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource), ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") { name }
                ... @defer(label: "i") { __type(name: "Product") { name } }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(0, run.Deferred.PendingCount);
    }

    [Fact]
    public async Task Defer_Should_SendIntrospectionWithTheGroup_When_ARootGroupAlsoSelectsALookupField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["A:2", "B:3"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") { name }
                ... @defer(label: "i") {
                    __type(name: "Product") { name }
                    product(id: "1") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(1, run.Deferred.PendingCount);
    }

    [Fact]
    public async Task Defer_Should_SendTheRootTypenameWithTheGroup_When_ARootGroupSelectsItNextToALookupField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["A:2", "B:3"],
            ("A", ProductListSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") { name }
                ... @defer(label: "t") {
                    __typename
                    product(id: "1") { price }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(1, run.Deferred.PendingCount);
    }

    [Fact]
    public async Task Defer_Should_SendIntrospection_When_TheOperationSelectsNothingElse()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource), ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { ... @defer(label: "i") { __type(name: "Product") { name } } }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(0, run.Deferred.PendingCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendIntrospection_When_TheRootGroupHasASwitch(bool d)
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource), ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            query($d: Boolean!) {
                product(id: "1") { name }
                ... @defer(if: $d, label: "i") { __type(name: "Product") { name } }
            }
            """,
            new Dictionary<string, object?> { ["d"] = d });

        // assert
        MatchRun(run, $"d={d}");
        Assert.Equal(0, run.Deferred.PendingCount);
    }

    [Fact]
    public async Task Defer_Should_MergeIntrospectionSelections_When_PlainAndDeferredSelectTheSameField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource), ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") { name }
                __type(name: "Product") { name }
                ... @defer(label: "i") { __type(name: "Product") { kind } }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(0, run.Deferred.PendingCount);
    }

    [Fact]
    public async Task Defer_Should_AnnounceTheNestedGroupWithTheFirstPayload_When_TheOuterGroupIsOnlyIntrospection()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource), ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                ... @defer(label: "o") {
                    __type(name: "Product") { name }
                    ... @defer(label: "i") { product(id: "1") { name } }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(0, run.Deferred.IndexOfPending("i"));
        Assert.Equal(-1, run.Deferred.IndexOfPending("o"));
        Assert.Contains("\"__type\"", run.Deferred.Payloads[0]);
    }

    [Fact]
    public async Task Defer_Should_SendIntrospectionOnce_When_TheSameFieldIsSelectedOutsideAndInsideTheGroup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource), ("C", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                __type(name: "Product") { name }
                ... @defer(label: "i") { __type(name: "Product") { name } }
            }
            """,
            []);

        // assert
        MatchRun(run);
        Assert.Equal(0, run.Deferred.PendingCount);
    }

    [Fact]
    public async Task Defer_Should_AnnounceOneIdPerItem_When_TheAnchorIsAListOfThreeItems()
    {
        // arrange
        var gateway = await CreateGatewayAsync(("A", ProductListSource), ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResult_When_TheListOfTheAnchorIsEmpty()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, _, response) => schema is "A"
                ? ProductsList().Replace(response, "\"products\":[]")
                : response,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [GeneratedRegex("\"products\":\\[.*\\]")]
    private static partial Regex ProductsList();

    [Fact]
    public async Task Defer_Should_ReturnASingleResult_When_TheAnchorFieldReturnsNull()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { product(id: ID!): Product @lookup @null }
                type Product @key(fields: "id") { id: ID! name: String! }
                """),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheGroupWithTheNonNullError_When_TheParentStepAnswersNullForTheKey()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { id price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_FailOnlyTheInstanceOfTheNullKeyItem_When_TheParentStepAnswersNullForTheKeyOfOneItem()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "d") { id price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_FailOnlyTheInstanceOfTheNullKeyItem_When_TheNullKeyIsNotSelectedByTheClient()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstSku,
            failingSchema: null,
            configure: null,
            ("A", ProductTwoKeysSource),
            ("B", ProductBySkuSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "d") { id price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheGroupWithTheNonNullError_When_AnObjectItRequiresHoldsANull()
    {
        // arrange
        // the parent step reads package.weight for shipping, which only the fragment selects
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstWeight,
            failingSchema: null,
            configure: null,
            ("A", ProductPackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { package { weight height } shipping } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheGroupWithTheNonNullError_When_AListItRequiresHoldsANull()
    {
        // arrange
        // the parent step reads the weight of each parcel for total, which only the fragment selects
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstWeight,
            failingSchema: null,
            configure: null,
            ("A", ProductPackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { parcels { weight height } total } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheGroupWithTheNonNullError_When_TheKeyIsNullAndAFieldIsRenamed()
    {
        // arrange
        // Book.size and Pen.size differ in nullability, so the gateway renames size in the request to A
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ItemSource),
            ("B", BookPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                item(id: "1") {
                    __typename
                    ... on Book { size title }
                    ... on Pen { size title }
                    ... on Book @defer(label: "d") { id price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheOuterGroupWithTheNonNullError_When_ANestedGroupReadsTheNullKeyToo()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "o") { id price ... @defer(label: "i") { rating } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialFieldRequiresTheNullField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstWeight,
            failingSchema: null,
            configure: null,
            ("A", ProductWeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { weight } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialFieldRequiresANullInADeferredObject()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstWeight,
            failingSchema: null,
            configure: null,
            ("A", ProductPackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialLookupReadsTheNullKey()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name price ... @defer(label: "d") { id rating } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialLookupReadsTheNullKeyOfOneItem()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name price ... @defer(label: "d") { id rating } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_ASiblingGroupReadsTheNullKey()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { id } ... @defer(label: "b") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_TheOuterGroupReadsTheNullKeyOfANestedGroup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "o") { price ... @defer(label: "i") { id rating } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_ASiblingGroupRequiresANullInASharedObject()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstWeight,
            failingSchema: null,
            configure: null,
            ("A", ProductPackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { package { weight height } }
                    ... @defer(label: "b") { package { height } shipping }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialFieldRequiresANullFieldOfALookup()
    {
        // arrange
        // the weight comes from a lookup of the initial payload, and only the fragment selects it
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) => schema is "B" ? WeightValue().Replace(text, "\"weight\":null") : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", WeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { weight } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheGroupWithTheNonNullError_When_TheKeyIsNullAndTheIfVariableIsTrue()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstId,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            query($v: Boolean!) { product(id: "1") { name ... @defer(if: $v, label: "d") { id price } } }
            """,
            new Dictionary<string, object?> { ["v"] = true });

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReportTheErrorOnce_When_AnInitialFieldRequiresTheNullFieldAndNullsStay()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: NullTheFirstWeight,
            failingSchema: null,
            configure: builder => builder.ModifyRequestOptions(
                o => o.DefaultErrorHandlingMode = HotChocolate.Language.ErrorHandlingMode.Null),
            ("A", ProductWeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { weight } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialFieldRequiresANullInAnObjectOfALookup()
    {
        // arrange
        // a lookup of the initial payload answers package.weight for shipping, the client selects no weight
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) => schema is "B" ? WeightValue().Replace(text, "\"weight\":null") : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_AnInitialFieldRequiresANullInAnObjectOfOneItem()
    {
        // arrange
        // a lookup of the initial payload answers package.weight for shipping, and null for the first item
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) => schema is "B" ? WeightValue().Replace(text, "\"weight\":null", 1) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_ALookupFailsAndAnInitialFieldRequiresItsRows()
    {
        // arrange
        // every request to B fails, and shipping of the initial payload requires package.weight from B
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: null,
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_ALookupFailsAndEveryItemRequiresItsRows()
    {
        // arrange
        // every request to B fails, and shipping of every item of the initial payload requires package.weight from B
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: null,
            ("A", ProductListSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheFragmentWithTheError_When_TheLookupFailsAndOnlyTheFragmentRequiresItsRows()
    {
        // arrange
        // every request to B fails, and shipping requires package.weight inside the fragment that selects the package
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: null,
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { package { height } shipping } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheFragmentWithTheError_When_TheNullFieldIsNoRequirementOfAnInitialField()
    {
        // arrange
        // shipping of the initial payload requires package.weight, and B answers null for the height of the package
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) => schema is "B" ? HeightValue().Replace(text, "\"height\":null") : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReportTheErrorOnce_When_ALookupFailsAndAnInitialFieldRequiresItsRowsAndNullsStay()
    {
        // arrange
        // every request to B fails, and shipping of the initial payload requires package.weight from B
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: builder => builder.ModifyRequestOptions(
                o => o.DefaultErrorHandlingMode = HotChocolate.Language.ErrorHandlingMode.Null),
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_ReportEachErrorOnce_When_ALookupFailsAndEveryItemRequiresItsRowsAndNullsStay()
    {
        // arrange
        // every request to B fails, and shipping of every item of the initial payload requires package.weight from B
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "B",
            configure: builder => builder.ModifyRequestOptions(
                o => o.DefaultErrorHandlingMode = HotChocolate.Language.ErrorHandlingMode.Null),
            ("A", ProductListSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendNullForTheFailedField_When_OnlyTheLookupOfTheRequirementFailsAndNullsStay()
    {
        // arrange
        // step 4 answers package.weight for shipping of the initial payload, step 5 answers the package of the fragment
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: null,
            configure: builder => builder.ModifyRequestOptions(
                o => o.DefaultErrorHandlingMode = HotChocolate.Language.ErrorHandlingMode.Null),
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));
        _gate!.FailingStep = "B:4";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReportTheErrorOnce_When_ALookupFailsAndAnInitialFieldRequiresItsNullableField()
    {
        // arrange
        // every request to W fails, and shipping of the initial payload requires the nullable weight from W
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "W",
            configure: null,
            ("A", ProductSource),
            ("W", NullableWeightSource),
            ("S", NullableShippingSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { weight } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendNullForTheFailedField_When_OnlyTheLookupOfTheNullableRequirementFails()
    {
        // arrange
        // step 4 answers the weight for shipping of the initial payload, step 5 answers the weight of the fragment
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("W", NullableWeightSource),
            ("S", NullableShippingSource));
        _gate!.FailingStep = "W:4";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { weight } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheFragmentWithTheError_When_OnlyTheLookupOfTheFragmentFails()
    {
        // arrange
        // step 4 answers package.weight for shipping of the initial payload, step 5 answers the package of the fragment
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PackageSource),
            ("C", PackageShippingSource));
        _gate!.FailingStep = "B:5";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheErrorWithTheNullObject_When_ALookupFailsBelowANullableObjectOfTheFragment()
    {
        // arrange
        // every request to D fails, and shipping of the initial payload requires maybePack.h2 from D
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "D",
            configure: null,
            ("A", ProductSource),
            ("B", NestedPackSource),
            ("C", NestedPackShippingSource),
            ("D", PackHeightSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name shipNullable ... @defer(label: "d") { maybePack { h2 } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_ReturnASingleResultWithTheError_When_ALookupFailsBelowNonNullObjectsUpToTheAnchor()
    {
        // arrange
        // every request to D fails, and shipping of the initial payload requires pack.h2 from D
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: null,
            failingSchema: "D",
            configure: null,
            ("A", ProductSource),
            ("B", NestedPackSource),
            ("C", NestedPackShippingSource),
            ("D", PackHeightSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { product(id: "1") { name shipPack ... @defer(label: "d") { pack { h2 } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheFragmentWithOnlyItsError_When_ANullableItemTookTheNullOfARequirement()
    {
        // arrange
        // the first part becomes null for its weight, which shipping requires, and B answers null for the height
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) => schema is "B"
                ? HeightValue().Replace(WeightValue().Replace(text, "\"weight\":null", 1), "\"height\":null")
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PackagePartsSource),
            ("C", PartsShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name shipping ... @defer(label: "d") { package { height parts { weight } } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [GeneratedRegex("\"height\":[^,}]*")]
    private static partial Regex HeightValue();

    private const string NickSource =
        """
        type Query { user: User }
        type User @key(fields: "id") { id: ID! nick: String }
        """;

    private const string InnerSource =
        """
        type Query { userById(id: ID!): User @lookup @internal }
        type User @key(fields: "id") { id: ID! inner: Inner innerNn: Inner! }
        type Inner { a: String! @error c: String }
        """;

    private const string InnerRequirementSource =
        """
        type Query { userById(id: ID!): User @lookup @internal }
        type User @key(fields: "id") {
            id: ID!
            cC(c: String @require(field: "inner.c")): String
            cA(a: String @require(field: "inner.a")): String
            cCNn(c: String @require(field: "innerNn.c")): String
        }
        """;

    private const string OtherFieldSource =
        """
        type Query { userById(id: ID!): User @lookup @internal }
        type User @key(fields: "id") { id: ID! other: String! }
        """;

    [Fact]
    public async Task Defer_Should_SendNullForTheDependentField_When_ItsRequirementLiesBelowAnObjectThatAnErrorNulled()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2@1"],
            ("A", NickSource),
            ("B", InnerSource),
            ("C", InnerRequirementSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            "{ user { nick ... @defer { inner { a } cC } } }",
            []);

        // assert
        run.AssertMatchesTwin(compareValues: true);
        MatchPayloads(run.Deferred);
    }

    [Fact]
    public async Task Defer_Should_SendNullForTheDependentField_When_TheClientDoesNotSelectTheObjectItRequires()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4@1"],
            ("A", NickSource),
            ("B", InnerSource),
            ("C", InnerRequirementSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            "{ user { nick ... @defer { cA } } }",
            []);

        // assert
        run.AssertMatchesTwin(compareValues: true);
        MatchPayloads(run.Deferred);
    }

    [Fact]
    public async Task Defer_Should_SendNullForTheDependentField_When_TheInitialPayloadSelectsTheNulledObject()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:5@1"],
            ("A", NickSource),
            ("B", InnerSource),
            ("C", InnerRequirementSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            "{ user { nick inner { a } ... @defer { cC } } }",
            []);

        // assert
        run.AssertMatchesTwin(compareValues: true);
        MatchPayloads(run.Deferred);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheSiblingField_When_AnotherFieldOfTheFragmentLacksItsRequirement()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2@1"],
            ("A", NickSource),
            ("B", InnerSource),
            ("C", InnerRequirementSource),
            ("D", OtherFieldSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            "{ user { nick ... @defer { inner { a } cC other } } }",
            []);

        // assert
        run.AssertMatchesTwin(compareValues: true);
        MatchPayloads(run.Deferred);
    }

    [Fact]
    public async Task Defer_Should_FailTheFragmentWithTheError_When_TheNonNullObjectOfTheRequirementIsNull()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2@1"],
            ("A", NickSource),
            ("B", InnerSource),
            ("C", InnerRequirementSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            "{ user { nick ... @defer { innerNn { a } cCNn } } }",
            []);

        // assert
        MatchPayloads(run);
    }

    // The source schema A answers null for the first id and the same name for every product.
    private static string NullTheFirstId(string schema, string body, string text)
        => schema is "A" && !body.Contains("__fusion", StringComparison.Ordinal)
            ? NormalizeName(schema, body, EntityId().Replace(text, "\"id\":null", 1))
            : text;

    // The source schema A answers null for the first sku and the same name for every product.
    private static string NullTheFirstSku(string schema, string body, string text)
        => schema is "A" && !body.Contains("__fusion", StringComparison.Ordinal)
            ? NormalizeName(schema, body, ProductSku().Replace(text, "\"sku\":null", 1))
            : text;

    [GeneratedRegex("\"sku\":\"[^\"]*\"")]
    private static partial Regex ProductSku();

    // The source schema A answers null for the first weight.
    private static string NullTheFirstWeight(string schema, string body, string text)
        => schema is "A" ? WeightValue().Replace(text, "\"weight\":null", 1) : text;

    private const string ProductPackageSource =
        """
        type Query { product(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! name: String! package: Package! parcels: [Package!]! }
        type Package { weight: Float! height: Float! }
        """;

    private const string PackageShippingSource =
        """
        type Query { productByIdForShipping(id: ID!): Product @lookup }
        type Product @key(fields: "id") {
            id: ID!
            shipping(weight: Float! @require(field: "package.weight")): Float!
            total(weights: [Float!]! @require(field: "parcels[weight]")): Float!
        }
        """;

    private const string PackageSource =
        """
        type Query { productByIdForPackage(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! package: Package! parcels: [Package!]! }
        type Package { weight: Float! height: Float! }
        """;

    private const string PackagePartsSource =
        """
        type Query { productByIdForPackage(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! package: Package! }
        type Package { height: Float! parts: [Part]! }
        type Part { weight: Float! }
        """;

    private const string PartsShippingSource =
        """
        type Query { productByIdForShipping(id: ID!): Product @lookup }
        type Product @key(fields: "id") {
            id: ID!
            shipping(weights: [Float]! @require(field: "package.parts[weight]")): Float!
        }
        """;

    private const string ItemSource =
        """
        type Query { item(id: ID!): Item book(id: ID!): Book @lookup }
        interface Item { id: ID! }
        type Book implements Item @key(fields: "id") { id: ID! size: Int! @shareable title: String! }
        type Pen implements Item { id: ID! size: Int title: String! }
        """;

    private const string BookPriceSource =
        """
        type Query { bookById(id: ID!): Book @lookup }
        type Book @key(fields: "id") { id: ID! price: Float! size: Int @shareable }
        """;

    [Fact]
    public async Task Defer_Should_AnnounceOneIdPerItem_When_TheAnchorIsInsideNestedLists()
    {
        // arrange
        var gateway = await CreateProductReviewGatewayAsync();

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { products { id reviews { id ... @defer(label: "r") { rating } } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_AnnounceTheItemsOfTheTypeCondition_When_TheListHoldsTwoBooksAndAMovie()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query {
                    items: [Item!]! @returns(types: ["Book", "Book", "Movie"])
                    book(id: ID!): Book @lookup
                }
                interface Item { id: ID! }
                type Book implements Item @key(fields: "id") { id: ID! title: String! }
                type Movie implements Item { id: ID! title: String! }
                """),
            ("B", """
                type Query { bookById(id: ID!): Book @lookup }
                type Book @key(fields: "id") { id: ID! rating: Int! }
                """));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { items { id ... on Book @defer(label: "b") { rating } } }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheFieldsOfBothDogs_When_TheFragmentHoldsTwoTypeConditionsAndOneNeedsALookup()
    {
        // arrange
        var gateway = await CreateDoubledTypeGatewayAsync("P:2", "A:3", "E:4");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { current { pets { ... @defer { ... on Dog { bark owner { name } } ... on Cat { meow } } } } }
            """,
            []);

        // assert
        MatchRunWithPayloadValues(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheFieldsOfEveryItem_When_TheFragmentHoldsTwoTypeConditionsBelowAListInAList()
    {
        // arrange
        var gateway = await CreateDoubledTypeGatewayAsync("P:2", "A:3", "E:4");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            { current { petGroups { ... @defer { ... on Dog { bark owner { name } } ... on Cat { meow } } } } }
            """,
            []);

        // assert
        MatchRunWithPayloadValues(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheOwnerOfEveryPet_When_BothTypeConditionsNeedALookup()
    {
        // arrange
        var gateway = await CreateDoubledTypeGatewayAsync("P:2", "A:3", "E:4");

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                current {
                    pets { ... @defer { ... on Dog { bark owner { name } } ... on Cat { meow owner { name } } } }
                }
            }
            """,
            []);

        // assert
        MatchRunWithPayloadValues(run);
    }

    // Matches the payloads with the values of the source schemas, and the request counts of the run and its twin.
    private static void MatchRunWithPayloadValues(TwinRun run)
    {
        run.AssertMatchesTwin(compareValues: true);

        var snapshot = Snapshot.Create();

        for (var i = 0; i < run.Deferred.Payloads.Count; i++)
        {
            snapshot.Add(run.Deferred.Payloads[i], $"Payload {i + 1}", "json");
        }

        snapshot.Add(
            $"deferred: {run.Deferred.RequestSummary}" + Environment.NewLine
            + $"plain twin: {run.Twin.RequestSummary}",
            "Requests",
            "text");
        snapshot.MatchMarkdownSnapshot();
    }

    private const string OverriddenAuthorPostSource =
        """
        type Query {
            feed: [Post]
            imagePostById(id: ID!): ImagePost @lookup @internal
            textPostById(id: ID!): TextPost @lookup @internal
            userById(id: ID!): User @lookup
        }

        interface Post { id: ID! author: User! reviewers: [User!]! }

        type ImagePost implements Post @key(fields: "id") { id: ID! author: User! reviewers: [User!]! }

        type TextPost implements Post @key(fields: "id") { id: ID! author: User! reviewers: [User!]! }

        type User @key(fields: "id") { id: ID! name: String! friends: [User!]! }
        """;

    private const string OverriddenAuthorMovedSource =
        """
        directive @override(from: String!) on FIELD_DEFINITION

        type Query {
            textPostById(id: ID!): TextPost @lookup @internal
            userById(id: ID!): User @lookup @internal
        }

        interface Post { id: ID! author: User! reviewers: [User!]! }

        type TextPost implements Post @key(fields: "id") {
            id: ID!
            author: User! @override(from: "A")
            reviewers: [User!]! @override(from: "A")
        }

        type User @key(fields: "id") { id: ID! email: String! }
        """;

    [Fact]
    public async Task Defer_Should_DeliverThePartOfEveryTypeBranch_When_ANestedFragmentLiesBelowASharedInterfaceField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:5>", "B:4"],
            ("A", OverriddenAuthorPostSource),
            ("B", OverriddenAuthorMovedSource));

        // act
        var run = await RunWithPerTypeTwinAsync(
            gateway,
            """
            {
                feed {
                    author {
                        friends { id }
                        ... @defer(label: "d") {
                            friends { id }
                            ... @defer(label: "n") { friends { email } }
                        }
                    }
                }
            }
            """,
            """
            {
                feed {
                    ... on ImagePost {
                        author { friends { id } ... { friends { id } ... { friends { email } } } }
                    }
                    ... on TextPost {
                        author { friends { id } ... { friends { id } ... { friends { email } } } }
                    }
                }
            }
            """);

        // assert
        MatchRunAgainstPerTypeTwin(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverThePartOfEveryTypeBranch_When_OneFragmentLiesBelowASharedInterfaceField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:5>", "B:4"],
            ("A", OverriddenAuthorPostSource),
            ("B", OverriddenAuthorMovedSource));

        // act
        var run = await RunWithPerTypeTwinAsync(
            gateway,
            """
            {
                feed {
                    author {
                        friends { id }
                        ... @defer(label: "n") { friends { email } }
                    }
                }
            }
            """,
            """
            {
                feed {
                    ... on ImagePost {
                        author { friends { id } ... { friends { email } } }
                    }
                    ... on TextPost {
                        author { friends { id } ... { friends { email } } }
                    }
                }
            }
            """);

        // assert
        MatchRunAgainstPerTypeTwin(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverThePartOfEveryTypeBranch_When_TheFragmentSelectsAnOwnFieldAndAPart()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["A:4", "B:5>", "B:6"],
            ("A", OverriddenAuthorPostSource),
            ("B", OverriddenAuthorMovedSource));

        // act
        var run = await RunWithPerTypeTwinAsync(
            gateway,
            """
            {
                feed {
                    author {
                        name
                        friends { id }
                        ... @defer(label: "d") { email friends { name } }
                    }
                }
            }
            """,
            """
            {
                feed {
                    ... on ImagePost {
                        author { name friends { id } ... { email friends { name } } }
                    }
                    ... on TextPost {
                        author { name friends { id } ... { email friends { name } } }
                    }
                }
            }
            """);

        // assert
        MatchRunAgainstPerTypeTwin(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverThePartOfEveryTypeBranch_When_ANestedFragmentLiesBelowASharedInterfaceList()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:5>", "B:4"],
            ("A", OverriddenAuthorPostSource),
            ("B", OverriddenAuthorMovedSource));

        // act
        var run = await RunWithPerTypeTwinAsync(
            gateway,
            """
            {
                feed {
                    reviewers {
                        friends { id }
                        ... @defer(label: "d") {
                            friends { id }
                            ... @defer(label: "n") { friends { email } }
                        }
                    }
                }
            }
            """,
            """
            {
                feed {
                    ... on ImagePost {
                        reviewers { friends { id } ... { friends { id } ... { friends { email } } } }
                    }
                    ... on TextPost {
                        reviewers { friends { id } ... { friends { id } ... { friends { email } } } }
                    }
                }
            }
            """);

        // assert
        MatchRunAgainstPerTypeTwin(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverThePartOfEveryTypeBranch_When_OneFragmentLiesBelowASharedInterfaceList()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:5>", "B:4"],
            ("A", OverriddenAuthorPostSource),
            ("B", OverriddenAuthorMovedSource));

        // act
        var run = await RunWithPerTypeTwinAsync(
            gateway,
            """
            {
                feed {
                    reviewers {
                        friends { id }
                        ... @defer(label: "n") { friends { email } }
                    }
                }
            }
            """,
            """
            {
                feed {
                    ... on ImagePost {
                        reviewers { friends { id } ... { friends { email } } }
                    }
                    ... on TextPost {
                        reviewers { friends { id } ... { friends { email } } }
                    }
                }
            }
            """);

        // assert
        MatchRunAgainstPerTypeTwin(run);
    }

    private async Task<TwinRun> RunWithPerTypeTwinAsync(Gateway gateway, string query, string reference)
    {
        var deferred = await RunDeferredAsync(gateway, query, []);

        _gate?.Enabled = false;

        // The reference selects the fields once per type. The same selection written once for the interface
        // answers null for fields of one type branch without @defer.
        var twin = await RunAsync(gateway, reference, []);
        return new TwinRun(deferred, twin);
    }

    private static void MatchRunAgainstPerTypeTwin(TwinRun run)
    {
        run.AssertMatchesTwin(compareValues: true);

        var places = run.Deferred.Payloads
            .SelectMany(t => JsonNode.Parse(t)!["pending"]?.AsArray() ?? [])
            .Select(t => $"{t!["path"]!.ToJsonString()} {t["label"]}")
            .Order(StringComparer.Ordinal);
        var snapshot = Snapshot.Create();
        snapshot.Add(Canonical(ToShape(run.Deferred.Data)), "Merged data", "json");
        snapshot.Add(string.Join(Environment.NewLine, places), "Announced places", "text");

        for (var i = 0; i < run.Deferred.Payloads.Count; i++)
        {
            snapshot.Add(ToShape(run.Deferred.Payloads[i]), $"Payload {i + 1}", "json");
        }

        snapshot.Add(
            $"deferred: {run.Deferred.RequestSummary}" + Environment.NewLine
            + $"per type twin: {run.Twin.RequestSummary}",
            "Requests",
            "text");
        snapshot.MatchMarkdownSnapshot();
    }

    // The source schemas answer with distinct values per object, such as bark-5 for a dog and name-106 for the owner
    // of a cat.
    private async Task<Gateway> CreateDoubledTypeGatewayAsync(params string[] releaseOrder)
    {
        var gate = _gate = new ResponseGate(releaseOrder);
        var servers = new List<(string, TestServer)>();
        (string Name, Action<IRequestExecutorBuilder> Configure)[] sources =
        [
            ("A", builder => builder.AddQueryType<DoubledTypeRootSource.Query>()
                .AddType<DoubledTypeRootSource.Dog>()
                .AddType<DoubledTypeRootSource.Cat>()),
            ("E", builder => builder.AddQueryType<DoubledTypeNameSource.Query>()),
            ("P", builder => builder.AddQueryType<DoubledTypePetSource.Query>())
        ];

        foreach (var (name, configure) in sources)
        {
            HttpMessageInvoker? invoker = null;
            var server = CreateSourceSchema(
                name,
                configure,
                mockHttpResponse: async request =>
                {
                    await request.Content!.LoadIntoBufferAsync();
                    var body = await request.Content.ReadAsStringAsync();
                    await gate.EnterAsync(name, body);
                    return await invoker!.SendAsync(request, CancellationToken.None);
                });
            invoker = new HttpMessageInvoker(server.CreateHandler());
            servers.Add((name, server));
        }

        return await CreateCompositeSchemaAsync(
            [.. servers],
            configureApplication: app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Response.Body = new PayloadCountingStream(context.Response.Body, gate);
                    await next(context);
                });
                app.UseRouting();
                app.UseEndpoints(endpoint => endpoint.MapGraphQL());
            },
            configureGatewayBuilder: builder =>
            {
                builder
                    .ModifyPlannerOptions(options => options.EnableSinglePlanDefer = true)
                    .AddDiagnosticEventListener(_ => new StepDoneListener(gate));
            });
    }

    public static class DoubledTypeRootSource
    {
        public class Query
        {
            public Holder GetCurrent() => new();

            [Lookup, Internal]
            public User? GetUserById([ID] int id) => new(id);

            [Lookup, Internal]
            public Dog? GetDogById([ID] int id) => new(id);

            [Lookup, Internal]
            public Cat? GetCatById([ID] int id) => new(id);
        }

        public class Holder
        {
            public IPet[] GetPets() => [new Dog(5), new Cat(6), new Dog(7)];

            public IPet[][] GetPetGroups() => [[new Dog(5), new Cat(6)], [new Dog(7)], [new Cat(8), new Dog(9)]];
        }

        [InterfaceType("Pet")]
        public interface IPet
        {
            [ID]
            int Id { get; }

            User Owner { get; }
        }

        [ObjectType("Dog")]
        public record Dog([property: ID] int Id) : IPet
        {
            public User Owner => new(Id);
        }

        [ObjectType("Cat")]
        public record Cat([property: ID] int Id) : IPet
        {
            public User Owner => new(Id + 100);
        }

        public record User([property: ID] int Id);
    }

    public static class DoubledTypeNameSource
    {
        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById([ID] int id) => new(id);
        }

        public record User([property: ID] int Id)
        {
            public string Name => "name-" + Id;
        }
    }

    public static class DoubledTypePetSource
    {
        public class Query
        {
            [Lookup, Internal]
            public Dog? GetDogById([ID] int id) => new(id);

            [Lookup, Internal]
            public Cat? GetCatById([ID] int id) => new(id);
        }

        [ObjectType("Dog")]
        public record Dog([property: ID] int Id)
        {
            public string Bark => "bark-" + Id;
        }

        [ObjectType("Cat")]
        public record Cat([property: ID] int Id)
        {
            public string Meow => "meow-" + Id;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheSharedFieldWithTheLongestPath_When_AnOuterAndAnInnerFragmentSelectIt(
        bool outerStepFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            outerStepFirst ? ["B:3", "C:4>", "B:5", "C:6"] : ["B:5", "C:6>", "B:3", "C:4"],
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                ... @defer(label: "a") { product(id: "1") { author { bio } } }
                product(id: "1") { name ... @defer(label: "b") { author { bio } } }
            }
            """,
            []);

        // assert
        MatchRun(run, $"outerStepFirst={outerStepFirst}");
    }

    [Fact]
    public async Task Defer_Should_NotSendTheSharedFieldAgain_When_TheInnerGroupCompletesAfterTheOuterGroup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "C:4>", "D:5", "B:6", "C:7"],
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource),
            ("D", ReviewsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                ... @defer(label: "a") { product(id: "1") { author { bio } } }
                product(id: "1") { name ... @defer(label: "b") { author { bio } reviews { body } } }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheGroupAndKeepTheAnchor_When_ANonNullDeferredFieldFails()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", """
                type Query { featured: Product! product(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! name: String! }
                """),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! @error rating: Int! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { featured { name ... @defer(label: "d") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheSiblingGroup_When_TheTransportOfAnotherGroupFails(bool failingFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            failingFirst ? ["B:3>", "C:2"] : ["C:2>", "B:3"],
            rewrite: null,
            failingSchema: "B",
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { price }
                    ... @defer(label: "b") { reviews { body } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"failingFirst={failingFirst}");
    }

    [Theory]
    [InlineData("outerFirst")]
    [InlineData("innerFirst")]
    [InlineData("innerAuthorFirst")]
    [InlineData("failedStepFirst")]
    public async Task Defer_Should_DeliverTheSiblingGroup_When_TheInnerGroupFailsBelowTheSharedAnchor(string order)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            order switch
            {
                "outerFirst" => ["B:3", "C:4>", "D:5", "B:6", "C:7"],
                "innerFirst" => ["D:5", "B:6", "C:7>", "B:3", "C:4"],
                "failedStepFirst" => ["D:5", "B:3", "C:4>", "B:6", "C:7"],
                _ => ["B:6", "C:7", "D:5>", "B:3", "C:4"]
            },
            rewrite: null,
            failingSchema: "D",
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource),
            ("D", ReviewsSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                ... @defer(label: "a") { product(id: "1") { author { bio } } }
                product(id: "1") { name ... @defer(label: "b") { author { bio } reviews { body } } }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"order={order}");
    }

    private const string RootBadSource =
        """
        type Query { bad: String! x: String! }
        """;

    [GeneratedRegex("\"bad\":\"[^\"]*\"")]
    private static partial Regex BadValue();

    // The source answers the non-null field with null and no error.
    private static string NullTheBadField(string schema, string body, string text)
        => schema == "A" && body.Contains("bad") ? BadValue().Replace(text, "\"bad\":null") : text;

    private const string RootSlowSource =
        """
        type Query { slow: String! }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_DeliverTheSharedFieldWithTheNestedGroup_When_TheSiblingGroupFails(bool xFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            xFirst ? ["A:4>", "A:3", "B:2"] : ["A:4>", "B:2>", "A:3"],
            NullTheBadField,
            failingSchema: null,
            configure: null,
            ("A", RootBadSource),
            ("B", RootSlowSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { ... @defer(label: "R") { bad x } ... @defer(label: "P") { slow ... @defer(label: "C") { x } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"xFirst={xFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailBothGroups_When_TheSharedNonNullFieldFailsFirst(bool slowFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            slowFirst ? ["B:2>", "A:4>", "A:3"] : ["A:4>", "A:3", "B:2"],
            NullTheBadField,
            failingSchema: null,
            configure: null,
            ("A", RootBadSource),
            ("B", RootSlowSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { ... @defer(label: "R") { bad x } ... @defer(label: "P") { slow ... @defer(label: "C") { bad } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"slowFirst={slowFirst}");
    }

    [Fact]
    public async Task Defer_Should_ReportBothErrors_When_ANullableAndARequiredFieldOfTheGroupFail()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! nullable: String @error required: String! @error }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer { nullable required } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_NeverAnnounceTheChild_When_TheParentGroupFailsWhileTheChildRequestIsInFlight(
        bool childFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            childFirst ? ["C:3", "B:2>"] : ["B:2>", "C:3"],
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! bad: String! @error }
                """),
            ("C", ReviewsSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "P") { bad ... @defer(label: "C") { reviews { body } } }
                }
            }
            """,
            []);

        // assert
        Assert.Equal(1, run.PendingCount);
        MatchPayloads(run, $"childFirst={childFirst}");
    }

    [GeneratedRegex("\"x\":\"[^\"]*\"")]
    private static partial Regex SharedXValue();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_KeepTheFirstResult_When_SiblingGroupsAnswerASharedNonNullFieldWithAValueAndANull(
        bool nullFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            nullFirst ? ["B:2>", "B:3"] : ["B:3>", "B:2"],
            (schema, body, text) => schema == "B" && body.Contains("_2") && !body.Contains("_3")
                ? SharedXValue().Replace(text, "\"x\":null")
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"nullFirst={nullFirst}");
    }

    private const string RequirementWithErrorSource =
        """
        type Query { product(id: ID!): Product @lookup products: [Product!]! }
        type Product @key(fields: "id") { id: ID! name: String! weight: Float @error }
        """;

    private const string NullableWeightSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! weight: Float }
        """;

    private const string NestedPackSource =
        """
        type Query { productByIdForPack(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! pack: Pack! maybePack: Pack }
        type Pack @key(fields: "pid") { pid: ID! }
        """;

    private const string PackHeightSource =
        """
        type Query { packByPid(pid: ID!): Pack @lookup }
        type Pack @key(fields: "pid") { pid: ID! h2: Float! }
        """;

    private const string NestedPackShippingSource =
        """
        type Query { productByIdForShipping(id: ID!): Product @lookup }
        type Product @key(fields: "id") {
            id: ID!
            shipNullable(h: Float @require(field: "maybePack.h2")): Float
            shipPack(h: Float! @require(field: "pack.h2")): Float
        }
        """;

    private const string NullableShippingSource =
        """
        type Query { productByIdForShipping(id: ID!): Product @lookup }
        type Product @key(fields: "id") {
            id: ID!
            shipping(weight: Float @require(field: "weight")): Float
        }
        """;

    [Fact]
    public async Task Defer_Should_SendTheErrorWithTheGroupOfTheField_When_TheRequirementErrorIsReadInTheInitialPhase()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["A:3@2"],
            ("A", RequirementWithErrorSource),
            ("C", NullableShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { weight } ... @defer(label: "b") { shipping } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [GeneratedRegex("\"weight\":[^,}]*")]
    private static partial Regex WeightValue();

    [Fact]
    public async Task Defer_Should_SendTheNull_When_AStepWithoutGroupsWritesANullableRequirementAsNull()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["C:2>", "A:3"],
            (schema, body, text) => schema == "A" && body.Contains("weight")
                ? WeightValue().Replace(text, "\"weight\":null")
                : text,
            failingSchema: null,
            configure: null,
            ("A", """
                type Query { product(id: ID!): Product @lookup products: [Product!]! }
                type Product @key(fields: "id") { id: ID! name: String! weight: Float }
                """),
            ("C", NullableShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { weight } ... @defer(label: "b") { shipping } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    private sealed class ThrowingListener(string schema) : FusionExecutionDiagnosticEventListener
    {
        public override IDisposable ExecuteOperationNode(
            OperationPlanContext context,
            OperationExecutionNode node,
            string schemaName)
            => schemaName == schema
                ? throw new InvalidOperationException("The diagnostic listener faulted.")
                : base.ExecuteOperationNode(context, node, schemaName);
    }

    [Fact]
    public async Task Defer_Should_CompleteWithAnError_When_TheStepFailsWithoutARecordedError()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3@2"],
            rewrite: null,
            failingSchema: null,
            configure: builder => builder.AddDiagnosticEventListener(_ => new ThrowingListener("C")),
            ("A", ProductSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { price }
                    ... @defer(label: "b") { reviews { body } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteWithTheError_When_TheMergeFailsAtTheDepthLimit()
    {
        // arrange
        const int depth = 33;
        var chain = string.Concat(Enumerable.Repeat("n { ", depth))
            + "w"
            + string.Concat(Enumerable.Repeat(" }", depth));
        var types = string.Join(
            "\n",
            Enumerable.Range(1, 40).Select(i => i == 40
                ? $"type N{i} {{ v: Int! w: Float! }}"
                : $"type N{i} {{ n: N{i + 1}! v: Int! w: Float! }}"));
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! n: N1! }
                """ + "\n" + types));

        // act
        var run = await RunDeferredAsync(
            gateway,
            "{ product(id: \"1\") { name ... @defer(label: \"d\") { " + chain + " } } }",
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheErrorWithTheGroupOfTheField_When_UnlabelledSiblingsShareOneStep()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float @error rating: Int! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer { rating } ... @defer { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_NullTheNullableItem_When_ARequiredFieldOfTheItemFailsInsideTheGroup()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("C", """
                type Query { productByIdForReviews(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! reviews: [Review] }
                type Review { body: String! @error }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "r") { reviews { body } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_KeepTheObjectsOfTheInitialPayload_When_ARequiredFieldBelowTheAnchorFails()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ("A", ProductSource),
            ("B", ReviewKeySource),
            ("C", """
                type Query { reviewById(id: ID!): Review @lookup }
                type Review @key(fields: "id") { id: ID! rating: Int! @error }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name reviews { id } ... @defer(label: "b") { reviews { rating } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_ReportTheErrorOnce_When_GroupsAtDifferentAnchorsShareAFailingNonNullField(
        bool innerFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            innerFirst ? ["B:4>", "B:3"] : ["B:3>", "B:4"],
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! @error }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                ... @defer(label: "a") { product(id: "1") { price } }
                product(id: "1") { name ... @defer(label: "b") { price } }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"innerFirst={innerFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_KeepTheFirstResult_When_ALaterAnswerOfTheSharedNonNullFieldIsAFailedEntity(
        bool valueFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            valueFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "{\"errors\":[{\"message\":\"The entity failed.\",\"path\":[\"productById\",\"x\"]}],"
                    + "\"data\":{\"productById\":null}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"valueFirst={valueFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_KeepTheFirstResult_When_ALaterAnswerOfTheSharedFieldIsAMissingEntity(
        bool valueFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            valueFirst ? ["W:4>", "W:5"] : ["W:5>", "W:4"],
            (schema, body, text) => schema == "W" && StepId().Match(body).Groups[1].Value == "5"
                ? "{\"data\":{\"productById\":null}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("W", NullableWeightSource),
            ("S", NullableShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { weight } ... @defer(label: "b") { shipping } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"valueFirst={valueFirst}");
    }

    private const string SharedAuthorSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! author: Author }
        type Author { id: ID! bio: String! age: Int! note: String }
        """;

    private const string SharedAuthorTagsSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! author: Author }
        type Author { id: ID! tags: [String] age: Int! }
        """;

    private const string SharedAuthorListSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! authors: [Author] }
        type Author { id: ID! bio: String! }
        """;

    private const string FailedEntity =
        "{\"errors\":[{\"message\":\"The entity failed.\",\"path\":[\"productById\"]}],"
        + "\"data\":{\"productById\":null}}";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailTheGroup_When_ItsEntityFailsBelowAnObjectOfTheSiblingGroup(bool objectFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            objectFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2" ? FailedEntity : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"objectFirst={objectFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailTheGroup_When_ItsEntityIsMissingBelowAnObjectOfTheSiblingGroup(bool objectFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            objectFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "{\"data\":{\"productById\":null}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"objectFirst={objectFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendNullForTheNullableField_When_ItsEntityFailsBelowAnObjectOfTheSiblingGroup(
        bool withError)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? withError ? FailedEntity : "{\"data\":{\"productById\":null}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { note } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"withError={withError}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailTheGroup_When_TheSourceAnswersNullForAnObjectOfTheSiblingGroup(bool withError)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? withError
                    ? "{\"errors\":[{\"message\":\"The author failed.\",\"path\":[\"productById\",\"author\"]}],"
                        + "\"data\":{\"productById\":{\"author\":null}}}"
                    : "{\"data\":{\"productById\":{\"author\":null}}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"withError={withError}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailTheInstanceOfTheItem_When_ItsEntityIsMissingBelowAnObjectOfTheSiblingGroup(
        bool objectFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            objectFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? string.Join(
                    "\n",
                    text.Split('\n').Select(line => line.Contains("\"variableIndex\":1,")
                        ? "{\"variableIndex\":1,\"data\":{\"productById\":null}}"
                        : line))
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "a") { author { id } } ... @defer(label: "b") { author { bio } } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"objectFirst={objectFirst}");
    }

    [Theory]
    [InlineData("missingEntity", true)]
    [InlineData("missingEntity", false)]
    [InlineData("nullList", true)]
    [InlineData("nullItem", true)]
    [InlineData("nullItem", false)]
    [InlineData("nullItemWithError", true)]
    [InlineData("nullFieldOfItem", true)]
    [InlineData("nullFieldOfItem", false)]
    public async Task Defer_Should_FailTheGroup_When_ItsAnswerIsNullBelowAListOfTheSiblingGroup(
        string answer,
        bool listFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            listFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) =>
            {
                if (schema != "B" || StepId().Match(body).Groups[1].Value != "2")
                {
                    return text;
                }

                // The first item of the list of the source is replaced.
                var start = text.IndexOf("[{", StringComparison.Ordinal) + 1;
                var end = text.IndexOf('}', start) + 1;

                return answer switch
                {
                    "missingEntity" => "{\"data\":{\"productById\":null}}",
                    "nullList" => "{\"data\":{\"productById\":{\"authors\":null}}}",
                    "nullItem" => text[..start] + "null" + text[end..],
                    "nullItemWithError" =>
                        "{\"errors\":[{\"message\":\"The bio failed.\","
                        + "\"path\":[\"productById\",\"authors\",0,\"bio\"]}],"
                        + text[1..start] + "null" + text[end..],
                    _ => text[..start] + "{\"bio\":null}" + text[end..]
                };
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorListSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { authors { id } }
                    ... @defer(label: "b") { authors { bio } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"answer={answer}, listFirst={listFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailTheGroup_When_ItsEntityFailsTwoObjectsBelowTheAnchor(bool withError)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? withError ? FailedEntity : "{\"data\":{\"productById\":null}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! author: Author }
                type Author { id: ID! publisher: Publisher }
                type Publisher { id: ID! title: String! city: String }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { publisher { id } } }
                    ... @defer(label: "b") { author { publisher { title city } } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"withError={withError}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_KeepTheObjectOfTheSiblingGroup_When_ANonNullFieldOfTheGroupFailsBelowIt(
        bool objectFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            objectFirst ? ["B:3>", "B:2"] : ["B:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "{\"data\":{\"productById\":{\"author\":{\"bio\":null}}}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"objectFirst={objectFirst}");
    }

    [Fact]
    public async Task Defer_Should_DeliverTheThirdGroup_When_ANonNullFieldOfTheSecondGroupFailsBelowTheSharedObject()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? "{\"data\":{\"productById\":{\"author\":{\"bio\":null}}}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                    ... @defer(label: "c") { author { age } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheValue_When_TheRequestOfTheNestedFragmentAnswersBeforeItsParentsRequest()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            NestedAnswerQuery,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheValue_When_TheNestedRequestOfAListAnswersBeforeItsParentsRequest()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            ("A", ProductListSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                products {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") {
                        author { bio }
                        ... @defer(label: "x") { author { bio age } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheValue_When_TheRequestOfTheDeepestNestedFragmentAnswersFirst()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4", "B:3", "B:2>", "B:5"],
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") {
                        author { bio }
                        ... @defer(label: "x") {
                            author { bio age }
                            ... @defer(label: "y") { author { bio age note } }
                        }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheValue_When_TheRequestOfAnUnlabelledNestedFragmentAnswersBeforeItsParent()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") {
                        author { bio }
                        ... @defer { author { bio age } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheValue_When_TheRequestOfANullableNestedFieldAnswersBeforeItsParentsRequest()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") {
                        author { bio }
                        ... @defer(label: "x") { author { bio note } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_KeepTheRowOfTheEnclosingFragment_When_TheNestedRequestAnswersFirstWithAnExtraField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? text.Replace("\"author\":{", "\"author\":{\"bio\":\"other\",", StringComparison.Ordinal)
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            NestedAnswerQuery,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_KeepTheListOfTheEnclosingFragment_When_TheNestedRequestAnswersFirstWithAValueForIt()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? text.Replace("\"author\":{", "\"author\":{\"tags\":[\"other\"],", StringComparison.Ordinal)
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorTagsSource));

        // act
        var run = await RunWithTwinAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") {
                        author { tags }
                        ... @defer(label: "x") { author { tags age } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchRun(run);
    }

    [Fact]
    public async Task Defer_Should_NotAnnounceTheNestedFragment_When_ItsParentFailsAfterTheNestedRequestAnswered()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3", "B:2>", "B:4"],
            ("A", ProductSource),
            ("B", SharedAuthorSource));
        _gate!.FailingStep = "B:2";

        // act
        var run = await RunDeferredAsync(gateway, NestedAnswerQuery, []);

        // assert
        MatchPayloads(run);
    }

    private const string NestedAnswerQuery =
        """
        {
            product(id: "1") {
                name
                ... @defer(label: "a") { author { id } }
                ... @defer(label: "b") {
                    author { bio }
                    ... @defer(label: "x") { author { bio age } }
                }
            }
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_CompleteTheNestedFragmentWithTheError_When_ItsRequestAnswersNullForTheObject(
        bool nestedFirst)
    {
        // arrange
        const string answer = "{\"data\":{\"productById\":{\"author\":null}}}";

        // act
        // the nested request answers before or after the request of its parent
        var run = await RunNestedAnswerAsync(nestedFirst, answer);

        // assert
        MatchNestedAnswer(run, nestedFirst);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_CompleteTheNestedFragmentWithTheError_When_ItsRequestAnswersAnEntityError(
        bool nestedFirst)
    {
        // act
        // the nested request answers before or after the request of its parent
        var run = await RunNestedAnswerAsync(nestedFirst, FailedEntity);

        // assert
        MatchNestedAnswer(run, nestedFirst);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_CompleteTheNestedFragmentWithTheError_When_ItsRequestAnswersAMissingEntity(
        bool nestedFirst)
    {
        // act
        // the nested request answers before or after the request of its parent
        var run = await RunNestedAnswerAsync(nestedFirst, MissingEntity);

        // assert
        MatchNestedAnswer(run, nestedFirst);
    }

    private static void MatchNestedAnswer(Run run, bool nestedFirst)
    {
        // both orders deliver the same merged data and errors
        MatchMergedAnswer(run);

        // each order delivers its own payloads
        MatchPayloads(run, $"nestedFirst={nestedFirst}");
    }

    private static void MatchMergedAnswer(Run run)
    {
        var snapshot = Snapshot.Create();
        snapshot.Add(Canonical(run.Data), "Merged data", "json");
        snapshot.Add(run.Errors, "Errors", "json");
        snapshot.MatchMarkdownSnapshot();
    }

    // The request of the nested fragment x (B:3) answers with the given text, either before or after the request of
    // its parent b (B:2).
    private async Task<Run> RunNestedAnswerAsync(bool nestedFirst, string answer)
    {
        var gateway = await CreateGatewayAsync(
            nestedFirst ? ["B:3", "B:2>", "B:4"] : ["B:2>", "B:3", "B:4"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3" ? answer : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource));

        return await RunDeferredAsync(gateway, NestedAnswerQuery, []);
    }

    [Fact]
    public async Task Defer_Should_ReportTheMergeErrorAndTheErrorWithoutAPath_When_TheMergeFailsAtTheDepthLimit()
    {
        // arrange
        const int depth = 33;
        var chain = string.Concat(Enumerable.Repeat("n { ", depth))
            + "w"
            + string.Concat(Enumerable.Repeat(" }", depth));
        var types = string.Join(
            "\n",
            Enumerable.Range(1, 40).Select(i => i == 40
                ? $"type N{i} {{ v: Int! w: Float! }}"
                : $"type N{i} {{ n: N{i + 1}! v: Int! w: Float! }}"));
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B" ? AddErrorWithoutPath(text) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! n: N1! }
                """ + "\n" + types));

        // act
        var run = await RunDeferredAsync(
            gateway,
            "{ product(id: \"1\") { name ... @defer(label: \"d\") { " + chain + " } } }",
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheNullableFieldError_When_AnotherStepFailsInTransportAfterItsFieldWasDelivered()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4>", "P:3", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """),
            ("P", """
                type Query { productByIdP(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float @error }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteWithoutAnError_When_TheStepFailsInTransportAfterTheSharedFieldWasDelivered()
    {
        // arrange
        var transportErrors = new List<string>();
        var gateway = await CreateGatewayAsync(
            ["B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: builder => builder.AddDiagnosticEventListener(_ => new TransportErrorListener(transportErrors)),
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x } } }
            """,
            []);

        // assert
        Assert.Equal(["B:2"], transportErrors);
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheOtherField_When_TheStepFailsInTransportAfterTheSharedFieldWasDelivered()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4>", "P:3", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendTheEntityErrorOnce_When_UnlabelledSiblingsShareTheStepAndOneFailsOnAnotherField()
    {
        // arrange
        // The steps are B:5 for a, B:2 for both unlabelled fragments and P:4 for the price of the first one.
        var gateway = await CreateGatewayAsync(
            ["B:5>", "P:4", "B:2"],
            (schema, body, text) => schema switch
            {
                "P" => PriceValue().Replace(text, "\"price\":null"),
                "B" when StepId().Match(body).Groups[1].Value == "2" => FailedEntity,
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer { author { note } price }
                    ... @defer { author { note } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendNoError_When_TheStepOfADroppedFragmentFailsBeforeTheLastFragmentCompletes()
    {
        // arrange
        // the rating goes out under top, so inner is dropped and its step C:6 fails before last completes
        var transportErrors = new List<string>();
        var gateway = await CreateGatewayAsync(
            ["B:3", "C:4>", "B:5>", "C:6", "D:2"],
            rewrite: null,
            failingSchema: null,
            configure: builder => builder.AddDiagnosticEventListener(_ => new TransportErrorListener(transportErrors)),
            [
                .. s_productReviewSources,
                ("D", """
                    type Query { productByIdD(id: ID!): Product @lookup }
                    type Product @key(fields: "id") { id: ID! title: String! }
                    """)
            ]);
        _gate!.FailingStep = "C:6";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                ... @defer(label: "top") { products { reviews { rating } } }
                products {
                    id
                    ... @defer(label: "outer") {
                        reviews { id ... @defer(label: "inner") { rating } }
                    }
                }
                ... @defer(label: "last") { products { title } }
            }
            """,
            []);

        // assert
        Assert.Equal(["C:6"], transportErrors);
        MatchPayloadShapes(run);
    }

    [Fact]
    public async Task Defer_Should_NotAnnounceTheNestedFragment_When_ItsOnlyFieldWasSentBeforeItsStepFailed()
    {
        // arrange
        // b sends the bio, then the step C:6 of c fails before the price lets a complete
        var transportErrors = new List<string>();
        var gateway = await CreateGatewayAsync(
            ["B:2", "C:3>", "B:4", "C:6", "P:5"],
            rewrite: null,
            failingSchema: null,
            configure: builder => builder.AddDiagnosticEventListener(_ => new TransportErrorListener(transportErrors)),
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource),
            ("P", SharedPriceSource));
        _gate!.FailingStep = "C:6";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id ... @defer(label: "c") { bio } } price }
                    ... @defer(label: "b") { author { bio } }
                }
            }
            """,
            []);

        // assert
        Assert.Equal(["C:6"], transportErrors);
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_AnnounceTheNestedFragment_When_ItsSourceReportsAnErrorAfterItsRowsWereSent()
    {
        // arrange
        // t sends the bio, then the step of g reports an error without a path before p completes
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:3", "P:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? AddErrorWithoutPath(text)
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(gateway, SentBioQuery, []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_NotAnnounceTheNestedFragment_When_ItsEntityIsMissingAfterItsRowsWereSent()
    {
        // arrange
        // t sends the bio, then the step of g answers a missing entity before p completes
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:3", "P:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? MissingEntity
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(gateway, SentBioQuery, []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendNullWithTheStepError_When_TheStepFailsAndLeavesARowWithoutAResult()
    {
        // arrange
        // t sends the bio, then the step of g fails in transport before p completes, and g also selects the note
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:3", "P:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { author { bio } }
                    ... @defer(label: "p") { price ... @defer(label: "g") { author { bio note } } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheNestedFragment_When_TheStepThatFeedsItsLookupFailsAfterTheParentsRowWasSent()
    {
        // arrange
        // t sends the author id, then the step B:2 of g, which feeds the lookup of c, fails in transport
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { author { id } }
                    ... @defer(label: "g") { author { id ... @defer(label: "c") { bio } } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_FailTheNestedFragment_When_TheStepThatFeedsItsLookupFailsAfterItWasAnnounced()
    {
        // arrange
        // g and c are announced with the price of p, then the step B:3 of g fails in transport
        var gateway = await CreateGatewayAsync(
            ["B:5>", "P:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { author { id } }
                    ... @defer(label: "p") {
                        price
                        ... @defer(label: "g") { author { id ... @defer(label: "c") { bio } } }
                    }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheSharedFieldOfBothNestedFragments_When_TheStepThatFeedsOneLookupFails()
    {
        // arrange
        // the step B:2 of h fails in transport and h completes before the step B:4 of g answers
        var gateway = await CreateGatewayAsync(
            ["B:6>", "B:2>", "B:4"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { author { id } }
                    ... @defer(label: "g") { author { id ... @defer(label: "c") { bio } } }
                    ... @defer(label: "h") { author { id ... @defer(label: "k") { bio } } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheNestedFragment_When_TheFailedStepOfItsParentDoesNotFeedItsStep()
    {
        // arrange
        // t sends x, the step B:2 of g fails in transport, then the price of c is fetched
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:2>", "P:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { x }
                    ... @defer(label: "g") { x ... @defer(label: "c") { price } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    private const string SentBioQuery =
        """
        {
            product(id: "1") {
                name
                ... @defer(label: "t") { author { bio } }
                ... @defer(label: "p") { price ... @defer(label: "g") { author { bio } } }
            }
        }
        """;

    [Fact]
    public async Task Defer_Should_ReportOnlyTheNullError_When_AnotherOperationOfTheBatchThatFeedsItsLookupHasNoResult()
    {
        // arrange
        // the batch of B answers the author of product and has no result for the author of topProduct
        var gateway = await CreateGatewayAsync(
            ["B:2", "C:5>", "C:4"],
            (schema, body, text) => schema switch
            {
                "B" when StepId().Matches(body).Any(m => m.Groups[1].Value == "2")
                    => string.Join("\n", text.Split('\n').Where(line => !line.Contains("\"requestIndex\":0"))),
                "C" when StepId().Match(body).Groups[1].Value == "4"
                    => NickValue().Replace(text, "\"nick\":null"),
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", BatchProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));

        // act
        var run = await RunDeferredAsync(gateway, BatchQuery, []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        Defer_Should_CompleteTheNestedFragmentsByTheirType_When_ABatchOperationHasNoResultAndTheirLookupsAreSkipped(
            bool nullable)
    {
        // arrange
        // the batch of B has no result for topProduct and answers the author of product without its key
        var gateway = await CreateGatewayAsync(
            [],
            (schema, body, text) => schema == "B" && StepId().Matches(body).Any(m => m.Groups[1].Value == "2")
                ? EntityId().Replace(
                    string.Join("\n", text.Split('\n').Where(line => !line.Contains("\"requestIndex\":0"))),
                    string.Empty)
                : text,
            failingSchema: null,
            configure: null,
            ("A", BatchProductSource),
            ("B", AuthorKeySource),
            ("C", nullable ? AuthorNullableBioAndNickSource : AuthorBioAndNickSource));

        // act
        var run = await RunDeferredAsync(gateway, BatchQuery, []);

        // assert
        MatchPayloads(run, $"nullable={nullable}");
    }

    [Fact]
    public async Task Defer_Should_CompleteTheSiblingWithTheNonNullError_When_TheRequirementOfItsStepIsNull()
    {
        // arrange
        // the step that fetches the weight for b (B:4) answers null, so that the step of shipping is skipped, and the
        // step of a (B:5) answers null after b completed
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:5"],
            (schema, _, text) => schema == "B" ? WeightValue().Replace(text, "\"weight\":null") : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", WeightSource),
            ("C", ShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { weight }
                    ... @defer(label: "b") { shipping }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_CompleteTheSiblingWithTheNonNullError_When_TheRequirementBelowASharedObjectIsNull()
    {
        // arrange
        // the step that fetches the code of the related product for b (A:2) answers null, so that the step of shipping
        // is skipped, and the step of a (A:4) answers null after b completed
        var gateway = await CreateGatewayAsync(
            ["A:2>", "A:4"],
            (schema, _, text) => schema == "A" ? RelatedCode().Replace(text, "\"code\":null") : text,
            failingSchema: null,
            configure: null,
            ("A", RelatedProductSource),
            ("C", RelatedShippingSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { related { code } }
                    ... @defer(label: "b") { related { shipping } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    private const string RelatedProductSource =
        """
        type Query { product(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! name: String! code: String! related: Product! }
        """;

    private const string RelatedShippingSource =
        """
        type Query { productByIdForShipping(id: ID!): Product @lookup }
        type Product @key(fields: "id") {
            id: ID!
            shipping(code: String! @require(field: "code")): Float!
        }
        """;

    [GeneratedRegex("\"code\":\"[^\"]*\"")]
    private static partial Regex RelatedCode();

    [Fact]
    public async Task Defer_Should_SendNullWithTheStepError_When_TheStepThatFeedsTheLookupOfANullableFieldFails()
    {
        // arrange
        // t sends the author id, then the step of g that feeds the lookup of the nullable bio fails in transport
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorNullableBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { author { id } }
                    ... @defer(label: "g") { author { id ... @defer(label: "c") { bio } } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        Defer_Should_WaitForTheStepOfTheSibling_When_ItsOwnStepLeavesTheKeyThatTheSiblingAlsoFetches(bool nested)
    {
        // arrange
        // the step of p (B:5) answers the author without its key, r completes, then the step that fetches the key for
        // q (B:3) answers it
        var gateway = await CreateGatewayAsync(
            ["B:5", "P:2>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "5"
                ? EntityId().Replace(text, string.Empty)
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource),
            ("P", SharedPriceSource));
        var nestedFragment = nested ? " ... @defer(label: \"c\") { bio }" : string.Empty;

        // act
        var run = await RunDeferredAsync(
            gateway,
            $$"""
            {
                product(id: "1") {
                    name
                    ... @defer(label: "p") { author { id{{nestedFragment}} } }
                    ... @defer(label: "q") { author { bio } }
                    ... @defer(label: "r") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"nested={nested}");
    }

    [Fact]
    public async Task Defer_Should_WaitForTheStepOfTheSiblingAndCompleteWithTheNonNullError_When_BothStepsLeaveTheKey()
    {
        // arrange
        // the step of p (B:5) and the step that fetches the key for q (B:3) answer the author without its key
        var gateway = await CreateGatewayAsync(
            ["B:5", "P:2>", "B:3"],
            (schema, _, text) => schema == "B" ? EntityId().Replace(text, string.Empty) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "p") { author { id } }
                    ... @defer(label: "q") { author { bio } }
                    ... @defer(label: "r") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task
        Defer_Should_WaitForTheStepOfTheSibling_When_ItsOwnStepLeavesTheRequirementThatTheSiblingAlsoFetches()
    {
        // arrange
        // the step of a (B:6) answers without the weight, r completes, then the step that fetches the weight for b
        // (B:5) answers it
        var gateway = await CreateGatewayAsync(
            ["B:6", "P:2>", "B:5"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "6"
                ? WeightValue().Replace(text, string.Empty)
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", WeightSource),
            ("C", ShippingSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { weight }
                    ... @defer(label: "b") { shipping }
                    ... @defer(label: "r") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task
        Defer_Should_WaitForTheStepOfTheNestedFragment_When_ItsOwnStepLeavesTheObjectThatTheNestedStepAlsoFetches()
    {
        // arrange
        // the step of b (B:3) answers without the author, d completes, then the step of x (B:4) answers the author
        var gateway = await CreateGatewayAsync(
            ["B:3", "P:2>", "B:4"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? "{\"data\":{\"productById\":{}}}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "b") {
                        author { note }
                        ... @defer(label: "x") { author { note age } }
                    }
                    ... @defer(label: "d") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        Defer_Should_CompleteTheRowWithoutTheStepError_When_AnotherStepFailsAndItsLookupIsSkippedForAMissingKey(
            bool nullable)
    {
        // arrange
        // B answers the author without its key, so that the lookup of bio is skipped, then the step of price fails
        var gateway = await CreateGatewayAsync(
            ["B:2", "P:3"],
            (schema, _, text) => schema switch
            {
                "B" => EntityId().Replace(text, string.Empty),
                "P" => "this is not json",
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", nullable ? AuthorNullableBioSource : AuthorBioSource),
            ("P", """
                type Query { productByIdP(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "g") { price author { bio } } } }
            """,
            []);

        // assert
        MatchPayloads(run, $"nullable={nullable}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        Defer_Should_CompleteTheRowByItsType_When_TheFragmentIsAtTheRootAndItsLookupIsSkippedForAMissingKey(
            bool nullable)
    {
        // arrange
        // B answers the author without its key and the lookup of bio is skipped
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B" ? EntityId().Replace(text, string.Empty) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", AuthorKeySource),
            ("C", nullable ? AuthorNullableBioSource : AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") { name }
                ... @defer(label: "o") { product(id: "1") { author { bio } } }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"nullable={nullable}");
    }

    [Theory]
    [InlineData("NullableObject", "author: Author")]
    [InlineData("NullableItem", "authors: [Author]!")]
    [InlineData("NonNullItem", "authors: [Author!]!")]
    [InlineData("NullableList", "authors: [Author!]")]
    public async Task
        Defer_Should_PropagateTheNullToTheNextNullablePosition_When_TheLookupBelowItIsSkippedForAMissingKey(
            string position,
            string field)
    {
        // arrange
        // B answers the first author without its key and the lookup of its non-null bio is skipped
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B" ? EntityId().Replace(text, string.Empty, 1) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", $$"""
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! {{field}} }
                type Author @key(fields: "id") { id: ID! }
                """),
            ("C", AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            $$"""
            { product(id: "1") { name ... @defer(label: "a") { {{field.Split(':')[0]}} { bio } } } }
            """,
            []);

        // assert
        MatchPayloads(run, position);
    }

    [Fact]
    public async Task Defer_Should_FailOnlyTheFragmentOfTheItem_When_TheLookupOfOneListItemIsSkippedForAMissingKey()
    {
        // arrange
        // B answers the first author without its key and the lookup of its non-null bio is skipped
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B" ? EntityId().Replace(text, string.Empty, 1) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! authors: [Author]! }
                type Author @key(fields: "id") { id: ID! }
                """),
            ("C", AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name authors { ... @defer(label: "a") { bio } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task
        Defer_Should_FailOnlyTheFragmentOfTheProduct_When_TheLookupBelowOneProductIsSkippedForAMissingKey()
    {
        // arrange
        // B answers the author of the first product without its key and the lookup of its non-null bio is skipped
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B" ? EntityId().Replace(text, string.Empty, 1) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "a") { author { bio } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task
        Defer_Should_FailTheNestedFragments_When_TheBatchThatFeedsTheirLookupsFailsAfterTheParentsRowWasSent()
    {
        // arrange
        // t sends the author id, then the batch of B that feeds the lookups of bio and nick fails in transport
        var gateway = await CreateGatewayAsync(
            ["B:6>", "B:2"],
            rewrite: null,
            failingSchema: null,
            configure: null,
            ("A", BatchProductSource),
            ("B", AuthorKeySource),
            ("C", AuthorBioAndNickSource));
        _gate!.FailingStep = "B:3";

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { author { id } }
                    ... @defer {
                        ... @include(if: true) { author { ... @defer(label: "bio") { bio } } }
                        ... @include(if: true) { author { ... @defer(label: "nick") { nick } } }
                    }
                }
                topProduct {
                    name
                    ... @defer { author { id } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendNullWithTheErrors_When_TheStepsOfTwoNullableFieldsFailInTransport()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["P:2", "R:3"],
            (schema, _, text) => schema is "P" or "R" ? "this is not json" : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("P", """
                type Query { productByIdP(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! cost: Float }
                """),
            ("R", """
                type Query { productByIdR(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! stars: Int }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "b") { cost stars } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task
        Defer_Should_SendNullWithTheError_When_TheErrorModeIsNullAndBothStepsFailAfterTheSharedFieldWasDelivered()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4>", "P:3", "B:2"],
            (schema, body, text) => schema == "P" || (schema == "B" && StepId().Match(body).Groups[1].Value == "2")
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: builder => builder.ModifyRequestOptions(
                o => o.DefaultErrorHandlingMode = HotChocolate.Language.ErrorHandlingMode.Null),
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_DeliverTheKey_When_TheStepOfTheFragmentFailsInTransportAndTheKeyWasAlreadyFetched()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["A:2"],
            (schema, body, text) => schema == "A" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { id } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_SendNullForTheNullableParent_When_TheStepOfItsNonNullFieldFailsInTransport()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:2", "C:3"],
            (schema, _, text) => schema == "C" ? "this is not json" : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! author: Author }
                type Author @key(fields: "id") { id: ID! }
                """),
            ("C", AuthorBioSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "g") { author { id bio } } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task
        Defer_Should_ReportOnlyTheNullError_When_AnotherStepFailsInTransportAfterTheSharedFieldWasDelivered()
    {
        // arrange
        // a sends x, then the step P:3 answers null for the non-null price before the step B:2 fails in transport
        var gateway = await CreateGatewayAsync(
            ["B:4>", "P:3", "B:2"],
            (schema, body, text) => schema switch
            {
                "B" when StepId().Match(body).Groups[1].Value == "2" => "this is not json",
                "P" => PriceValue().Replace(text, "\"price\":null"),
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "a") { x } ... @defer(label: "b") { x price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_ReportOnlyTheNullError_When_AStepFailsInTransportForAFieldThatAlreadyHoldsAValue()
    {
        // arrange
        // B:7 writes x and P answers null, then g's step B:2 fails for x after t completed
        var gateway = await CreateGatewayAsync(
            ["B:7", "P:3", "P:6", "P:8", "B:2@2"],
            (schema, body, text) => schema switch
            {
                "B" when StepId().Match(body).Groups[1].Value == "2" => "this is not json",
                "P" => PriceValue().Replace(text, "\"price\":null"),
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("P", SharedPriceSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! x: String! }
                """),
            ("C", """
                type Query { productByIdC(id: ID!): Product @lookup }
                type Product @key(fields: "id") {
                    id: ID!
                    total(price: Float @require(field: "price"), x: String @require(field: "x")): String!
                }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { price }
                    ... @defer(label: "g") { price x ... @defer(label: "c") { total } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [GeneratedRegex("\"nick\":\"[^\"]*\"")]
    private static partial Regex NickValue();

    private const string BatchProductSource =
        """
        type Query { product(id: ID!): Product @lookup topProduct: Product }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string BatchQuery =
        """
        {
            product(id: "1") {
                name
                ... @defer {
                    ... @include(if: true) { author { ... @defer(label: "bio") { bio } } }
                    ... @include(if: true) { author { ... @defer(label: "nick") { nick } } }
                }
            }
            topProduct {
                name
                ... @defer { author { id } }
            }
        }
        """;

    private const string SharedPriceSource =
        """
        type Query { productByIdP(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! price: Float! }
        """;

    private const string SharedAuthorNoteListSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! authors: [Author] }
        type Author { id: ID! note: String }
        """;

    private const string MissingEntity = "{\"data\":{\"productById\":null}}";

    private const string SharedBioQuery =
        """
        {
            product(id: "1") {
                name
                ... @defer(label: "a") { author { id } }
                ... @defer(label: "b") { author { bio } }
                ... @defer(label: "c") { author { bio } }
                ... @defer(label: "d") { price }
            }
        }
        """;

    // The steps are B:5 for a, B:4 for b, B:3 for c and P:2 for d. The payload of d lies between the answers of
    // b and c.
    private static readonly string[] s_sharedFieldOrder = ["B:5>", "B:4", "P:2>", "B:3"];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_KeepTheGroupOpen_When_ItsEntityIsMissingAndTheSiblingGroupStillFetchesTheField(
        bool withError)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            s_sharedFieldOrder,
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "4"
                ? withError ? FailedEntity : MissingEntity
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(gateway, SharedBioQuery, []);

        // assert
        MatchPayloads(run, $"withError={withError}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_FailBothGroups_When_TheEntityOfBothIsMissingBelowAnObjectOfAThirdGroup(
        bool withError)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            s_sharedFieldOrder,
            (schema, body, text) => schema != "B"
                ? text
                : StepId().Match(body).Groups[1].Value switch
                {
                    "4" => withError ? FailedEntity : MissingEntity,
                    "3" => MissingEntity,
                    _ => text
                },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(gateway, SharedBioQuery, []);

        // assert
        MatchPayloads(run, $"withError={withError}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendNullForTheSharedNullableField_When_TheEntityOfBothGroupsIsMissing(
        bool withError)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            s_sharedFieldOrder,
            (schema, body, text) => schema != "B"
                ? text
                : StepId().Match(body).Groups[1].Value switch
                {
                    "4" => withError ? FailedEntity : MissingEntity,
                    "3" => MissingEntity,
                    _ => text
                },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { note } }
                    ... @defer(label: "c") { author { note } }
                    ... @defer(label: "d") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"withError={withError}");
    }

    [Fact]
    public async Task Defer_Should_FailBothGroups_When_TheEntityOfOneIsMissingAndTheStepOfTheOtherFailsInTransport()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            s_sharedFieldOrder,
            (schema, body, text) => schema != "B"
                ? text
                : StepId().Match(body).Groups[1].Value switch
                {
                    "4" => MissingEntity,
                    "3" => "this is not json",
                    _ => text
                },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(gateway, SharedBioQuery, []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_KeepTheGroupOpen_When_ItsStepFailsInTransportAndTheSiblingGroupStillFetchesTheField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            s_sharedFieldOrder,
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "4"
                ? "this is not json"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(gateway, SharedBioQuery, []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_KeepOnlyTheInstanceOfTheItemOpen_When_ItsEntityIsMissingAndTheSiblingStillFetches()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:4>", "B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "3"
                ? string.Join(
                    "\n",
                    text.Split('\n').Select(line => line.Contains("\"variableIndex\":1,")
                        ? "{\"variableIndex\":1,\"data\":{\"productById\":null}}"
                        : line))
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", SharedAuthorSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                products {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                    ... @defer(label: "c") { author { bio } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Fact]
    public async Task Defer_Should_KeepTheGroupOpen_When_AnItemOfItsAnswerIsNullAndTheSiblingGroupStillFetchesTheList()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            s_sharedFieldOrder,
            (schema, body, text) =>
            {
                if (schema != "B" || StepId().Match(body).Groups[1].Value != "4")
                {
                    return text;
                }

                // The first item of the list of the source is replaced.
                var start = text.IndexOf("[{", StringComparison.Ordinal) + 1;
                var end = text.IndexOf('}', start) + 1;

                return text[..start] + "null" + text[end..];
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorListSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { authors { id } }
                    ... @defer(label: "b") { authors { bio } }
                    ... @defer(label: "c") { authors { bio } }
                    ... @defer(label: "d") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheSharedFieldWithTheGroupAtTheLongerPath_When_TheEntityOfOneGroupIsMissing(
        bool outerMissing)
    {
        // arrange
        // The steps are B:6 for a, B:5 for b, P:4 for d and A:2 and B:3 for o.
        var missing = outerMissing ? "3" : "5";
        var gateway = await CreateGatewayAsync(
            outerMissing ? ["B:6>", "B:3", "P:4>", "B:5"] : ["B:6>", "B:5", "P:4>", "B:3"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == missing
                ? MissingEntity
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                ... @defer(label: "o") { product(id: "1") { author { bio } } }
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { bio } }
                    ... @defer(label: "d") { price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"outerMissing={outerMissing}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_ReportOneError_When_AnItemOfItsAnswerIsNullWithAnErrorBelowAListOfTheSiblingGroup(
        bool nonNull)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3>", "B:2"],
            (schema, body, text) =>
            {
                if (schema != "B" || StepId().Match(body).Groups[1].Value != "2")
                {
                    return text;
                }

                // The first item of the list of the source is replaced.
                var start = text.IndexOf("[{", StringComparison.Ordinal) + 1;
                var end = text.IndexOf('}', start) + 1;

                return "{\"errors\":[{\"message\":\"The item failed.\","
                    + "\"path\":[\"productById\",\"authors\",0]}],"
                    + text[1..start] + "null" + text[end..];
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", nonNull ? SharedAuthorListSource : SharedAuthorNoteListSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            "{ product(id: \"1\") { name ... @defer(label: \"a\") { authors { id } } "
            + "... @defer(label: \"b\") { authors { " + (nonNull ? "bio" : "note") + " } } } }",
            []);

        // assert
        MatchPayloads(run, $"nonNull={nonNull}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_ReportTheErrorOfItsEntity_When_TheGroupFailedForAnotherReason(bool entityFirst)
    {
        // arrange
        // The steps are B:4 for a, and B:2 and P:3 for b.
        var gateway = await CreateGatewayAsync(
            entityFirst ? ["B:4>", "B:2", "P:3"] : ["B:4>", "P:3", "B:2"],
            (schema, body, text) => schema switch
            {
                "P" => PriceValue().Replace(text, "\"price\":null"),
                "B" when StepId().Match(body).Groups[1].Value == "2" => FailedEntity,
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { note } price }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"entityFirst={entityFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_ReportTheErrorOfItsEntity_When_TheFailedGroupSharesTheFieldWithASiblingGroup(
        bool siblingMissing)
    {
        // arrange
        // The steps are B:5 for a, B:3 and P:4 for b and B:2 for c.
        var gateway = await CreateGatewayAsync(
            ["B:5>", "P:4", "B:3>", "B:2"],
            (schema, body, text) => schema switch
            {
                "P" => PriceValue().Replace(text, "\"price\":null"),
                "B" => StepId().Match(body).Groups[1].Value switch
                {
                    "3" => FailedEntity,
                    "2" when siblingMissing => MissingEntity,
                    _ => text
                },
                _ => text
            },
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", SharedAuthorSource),
            ("P", SharedPriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { author { id } }
                    ... @defer(label: "b") { author { note } price }
                    ... @defer(label: "c") { author { note } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"siblingMissing={siblingMissing}");
    }

    // The source reports an error without a path in front of its other errors.
    private static string AddErrorWithoutPath(string text)
    {
        const string error = "{\"message\":\"The source failed.\"}";
        var errors = text.IndexOf("\"errors\":[", StringComparison.Ordinal);

        return errors < 0
            ? text.Insert(text.IndexOf('{') + 1, "\"errors\":[" + error + "],")
            : text.Insert(errors + 10, error + ",");
    }

    [Fact]
    public async Task Defer_Should_ReportTheErrorWithoutAPath_When_TheGroupFailsOnANonNullField()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B"
                ? AddErrorWithoutPath(PriceValue().Replace(text, "\"price\":null"))
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { product(id: "1") { name ... @defer(label: "d") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_ReportTheRootError_When_TheSourceOfTheFailedGroupAnswersWithoutData(
        bool failingFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            failingFirst ? ["B:3>", "C:2"] : ["C:2>", "B:3"],
            (schema, _, text) => schema == "B"
                ? "{\"errors\":[{\"message\":\"The source failed.\"}],\"data\":null}"
                : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", PriceSource),
            ("C", ReviewsSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { price }
                    ... @defer(label: "b") { reviews { body } }
                }
            }
            """,
            []);

        // assert
        MatchPayloads(run, $"failingFirst={failingFirst}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_SendTheErrorWithoutAPathWithItsGroup_When_UnlabelledSiblingsShareOneStep(
        bool failingFirst)
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) => schema == "B" ? AddErrorWithoutPath(text) : text,
            failingSchema: null,
            configure: null,
            ("A", ProductSource),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! @error rating: Int! }
                """));

        // act
        var run = await RunDeferredAsync(
            gateway,
            failingFirst
                ? """
                  { product(id: "1") { name ... @defer { price } ... @defer { rating } } }
                  """
                : """
                  { product(id: "1") { name ... @defer { rating } ... @defer { price } } }
                  """,
            []);

        // assert
        MatchPayloads(run, $"failingFirst={failingFirst}");
    }

    [Fact]
    public async Task Defer_Should_SendTheErrorWithoutAPathOnce_When_TheGroupHasAnInstancePerListItem()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            [],
            (schema, _, text) =>
            {
                if (schema != "B")
                {
                    return text;
                }

                var index = 0;
                return AddErrorWithoutPath(
                    PriceValue().Replace(text, m => index++ == 1 ? "\"price\":null" : m.Value));
            },
            failingSchema: null,
            configure: null,
            ("A", ProductListSource),
            ("B", PriceSource));

        // act
        var run = await RunDeferredAsync(
            gateway,
            """
            { products { name ... @defer(label: "p") { price } } }
            """,
            []);

        // assert
        MatchPayloads(run);
    }

    private const string MutationSource =
        """
        type Query { product(id: ID!): Product @lookup }
        type Mutation { createProduct: Product }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string ReviewBodySource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! reviews: [Review!]! }
        type Review { id: ID! body: String! }
        """;

    private const string ReviewKeySource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! reviews: [Review!]! topReview: Review }
        type Review @key(fields: "id") { id: ID! @shareable }
        """;

    private const string ReviewRatingSource =
        """
        type Query { reviewById(id: ID!): Review @lookup }
        type Review @key(fields: "id") { id: ID! rating: Int! }
        """;

    private const string ReviewStarsSource =
        """
        type Query { productByIdForReviews(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! reviews: [Review!]! }
        type Review { body: String! stars: Int! }
        """;

    private const string AuthorKeySource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! author: Author! }
        type Author @key(fields: "id") { id: ID! }
        """;

    private const string EditorKeySource =
        """
        type Query { productByIdForEditor(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! editor: Author! }
        type Author @key(fields: "id") { id: ID! }
        """;

    private const string AuthorAndEditorKeySource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! author: Author! editor: Author! }
        type Author @key(fields: "id") { id: ID! }
        """;

    private const string AuthorBioSource =
        """
        type Query { authorById(id: ID!): Author @lookup }
        type Author @key(fields: "id") { id: ID! bio: String! }
        """;

    private const string AuthorBioAndNickSource =
        """
        type Query { authorById(id: ID!): Author @lookup }
        type Author @key(fields: "id") { id: ID! bio: String! nick: String! }
        """;

    private const string AuthorNullableBioSource =
        """
        type Query { authorById(id: ID!): Author @lookup }
        type Author @key(fields: "id") { id: ID! bio: String }
        """;

    private const string AuthorNullableBioAndNickSource =
        """
        type Query { authorById(id: ID!): Author @lookup }
        type Author @key(fields: "id") { id: ID! bio: String nick: String }
        """;

    private const string WeightSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! weight: Float! }
        """;

    private const string ShippingSource =
        """
        type Query { productByIdForShipping(id: ID!): Product @lookup }
        type Product @key(fields: "id") {
            id: ID!
            shipping(weight: Float! @require(field: "weight")): Float!
        }
        """;

    private const string ProductListSource =
        """
        type Query { product(id: ID!): Product @lookup products: [Product!]! }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string ShelfSource =
        """
        type Query { product(id: ID!): Product @lookup shelf: Shelf }
        type Shelf { items: [Product!]! }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string ShelfTopSource =
        """
        type Query { product(id: ID!): Product @lookup shelf: Shelf }
        type Shelf { id: ID! top: Product! }
        type Product @key(fields: "id") { id: ID! name: String! related: Product }
        """;

    private const string ProductTwoKeysSource =
        """
        type Query {
            product(id: ID!): Product @lookup
            productBySkuA(sku: String!): Product @lookup
            products: [Product!]!
        }
        type Product @key(fields: "id") @key(fields: "sku") { id: ID! sku: String! name: String! }
        """;

    private const string ProductBySkuSource =
        """
        type Query { productBySku(sku: String!): Product @lookup }
        type Product @key(fields: "sku") { sku: String! price: Float! }
        """;

    private const string ProductSource =
        """
        type Query { product(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! name: String! }
        """;

    private const string PriceSource =
        """
        type Query { productById(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! price: Float! rating: Int! }
        """;

    private const string ReviewsSource =
        """
        type Query { productByIdForReviews(id: ID!): Product @lookup }
        type Product @key(fields: "id") { id: ID! reviews: [Review!]! }
        type Review { body: String! }
        """;

    private static readonly (string Name, string Sdl)[] s_productReviewSources =
    [
        ("A", """
            type Query { products: [Product!]! }
            type Product @key(fields: "id") { id: ID! @shareable }
            """),
        ("B", """
            type Query { productById(id: ID!): Product @lookup }
            type Product @key(fields: "id") { id: ID! reviews: [Review!]! }
            type Review @key(fields: "id") { id: ID! @shareable }
            """),
        ("C", """
            type Query { reviewById(id: ID!): Review @lookup }
            type Review @key(fields: "id") { id: ID! rating: Int! }
            """)
    ];

    private Task<Gateway> CreateProductReviewGatewayAsync(params string[] releaseOrder)
        => CreateGatewayAsync(releaseOrder, s_productReviewSources);

    // The second member of the result returns the number of requests to the source schema B so far that select the
    // reviews.
    private async Task<(Gateway Gateway, Func<int> ListRequests)> CreateReviewListGatewayAsync(string sourceA)
    {
        var count = 0;
        var gateway = await CreateGatewayAsync(
            [],
            rewrite: (schema, body, text) =>
            {
                if (schema == "B" && body.Contains("reviews"))
                {
                    Interlocked.Increment(ref count);
                }

                return text;
            },
            failingSchema: null,
            configure: null,
            ("A", sourceA),
            ("B", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! price: Float! reviews: [Review!]! }
                type Review @key(fields: "id") { id: ID! @shareable }
                """),
            ("C", """
                type Query { reviewById(id: ID!): Review @lookup }
                type Review @key(fields: "id") { id: ID! stars: Int! }
                """));
        return (gateway, () => Volatile.Read(ref count));
    }

    private Task<Gateway> CreateProductGatewayAsync(params string[] releaseOrder)
        => CreateGatewayAsync(releaseOrder, ("A", ProductSource), ("B", PriceSource), ("C", ReviewsSource));

    private Task<Gateway> CreateGatewayAsync(params (string Name, string Sdl)[] sources)
        => CreateGatewayAsync([], sources);

    private Task<Gateway> CreateGatewayAsync(string[] releaseOrder, params (string Name, string Sdl)[] sources)
        => CreateGatewayAsync(releaseOrder, rewrite: null, failingSchema: null, configure: null, sources);

    // The responses of the steps in releaseOrder ("schema:stepId") reach the gateway in that order. The rewrite
    // changes the response text of a source schema, and every request to the failing schema fails in transport.
    private Task<Gateway> CreateGatewayAsync(
        string[] releaseOrder,
        Func<string, string, string, string>? rewrite,
        string? failingSchema,
        Action<IFusionGatewayBuilder>? configure,
        params (string Name, string Sdl)[] sources)
        => CreateGatewayAsync(releaseOrder, rewrite, failingSchema, stalledSchema: null, configure, sources);

    // The requests to the stalled schema see the cancellation token of the gateway, and the step of the gate that
    // is marked as stalled is never answered. The rewrite and the failing schema do not apply to the stalled schema.
    private Task<Gateway> CreateGatewayAsync(
        string[] releaseOrder,
        Func<string, string, string, string>? rewrite,
        string? failingSchema,
        string? stalledSchema,
        Action<IFusionGatewayBuilder>? configure,
        params (string Name, string Sdl)[] sources)
    {
        var gate = _gate = new ResponseGate(releaseOrder);
        var servers = new List<(string, TestServer)>();

        foreach (var (name, sdl) in sources)
        {
            if (name == stalledSchema)
            {
                var stalling = new StallingHandler(gate, name);
                var stalledServer = CreateSourceSchema(name, sdl, httpClient: new HttpClient(stalling));
                stalling.InnerHandler = stalledServer.CreateHandler();
                servers.Add((name, stalledServer));
                continue;
            }

            HttpMessageInvoker? invoker = null;
            var server = CreateSourceSchema(
                name,
                sdl,
                mockHttpResponse: async request =>
                {
                    await request.Content!.LoadIntoBufferAsync();
                    var body = await request.Content.ReadAsStringAsync();
                    await gate.EnterAsync(name, body);

                    if (name == failingSchema || gate.Fails(name, body))
                    {
                        return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                    }

                    var response = await invoker!.SendAsync(request, CancellationToken.None);

                    if (rewrite is not null)
                    {
                        var text = rewrite(name, body, await response.Content.ReadAsStringAsync());
                        var contentType = response.Content.Headers.ContentType;
                        response.Content = new StringContent(text, System.Text.Encoding.UTF8);
                        response.Content.Headers.ContentType = contentType;
                    }

                    return response;
                });
            invoker = new HttpMessageInvoker(server.CreateHandler());
            servers.Add((name, server));
        }

        return CreateCompositeSchemaAsync(
            [.. servers],
            configureApplication: app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Response.Body = new PayloadCountingStream(context.Response.Body, gate);
                    await next(context);
                });
                app.UseRouting();
                app.UseEndpoints(endpoint => endpoint.MapGraphQL());
            },
            configureGatewayBuilder: builder =>
            {
                builder
                    .ModifyPlannerOptions(options => options.EnableSinglePlanDefer = true)
                    .AddDiagnosticEventListener(_ => new StepDoneListener(gate));
                configure?.Invoke(builder);
            });
    }

    // A source schema answers every lookup of a product with the same name.
    private static string NormalizeName(string schema, string body, string text)
        => ProductNameValue().Replace(text, "\"name\":\"s\"");

    [GeneratedRegex("\"name\":\"[^\"]*\"")]
    private static partial Regex ProductNameValue();

    private ResponseGate? _gate;

    // Holds the responses of the listed steps back. Each one after the first is released when the step
    // before it has handed its result to the executor and, when that step is listed with a trailing ">",
    // the gateway has written the next payload. A step listed as "schema:id@n" is also held until the gateway
    // has written n payloads in total.
    private sealed class ResponseGate
    {
        private static readonly TimeSpan s_guard = TimeSpan.FromSeconds(20);
        private readonly string[] _order;
        private readonly bool[] _waitForPayload;
        private readonly int[] _minPayloads;
        private readonly TaskCompletionSource[] _done;
        private readonly int[] _payloadsAtRelease;
        private readonly object _sync = new();
        private readonly List<(int Count, TaskCompletionSource Signal)> _payloadWaiters = [];
        private readonly List<string> _events = [];
        private readonly List<string> _cancelled = [];
        private int _payloads;

        public ResponseGate(string[] order)
        {
            _order = [.. order.Select(t => t.TrimEnd('>').Split('@')[0])];
            _waitForPayload = [.. order.Select(t => t.EndsWith('>'))];
            _minPayloads = [.. order.Select(t => t.TrimEnd('>').Split('@') is [_, var count] ? int.Parse(count) : 0)];
            _done =
            [
                .. order.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            ];
            _payloadsAtRelease = new int[order.Length];
        }

        public volatile bool Enabled = true;

        // The step ("schema:id") that fails in transport, and the step whose request is never answered.
        public volatile string? FailingStep;
        public volatile string? StalledStep;

        // The steps whose request the gateway cancelled while the source had not answered it.
        public IReadOnlyList<string> CancelledSteps
        {
            get
            {
                lock (_sync)
                {
                    return [.. _cancelled];
                }
            }
        }

        // The arrival of a request and the completion of a step in the order they happened.
        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_sync)
                {
                    return [.. _events];
                }
            }
        }

        public async Task EnterAsync(string schema, string body)
        {
            lock (_sync)
            {
                foreach (var id in StepId().Matches(body).Select(m => m.Groups[1].Value))
                {
                    _events.Add($"enter {schema}:{id}");
                }
            }

            var key = StepId().Matches(body)
                .Select(m => schema + ":" + m.Groups[1].Value)
                .FirstOrDefault(_order.Contains);

            if (!Enabled || key is null)
            {
                return;
            }

            var index = Array.IndexOf(_order, key);

            if (index > 0)
            {
                await _done[index - 1].Task.WaitAsync(s_guard);

                if (_waitForPayload[index - 1])
                {
                    await PayloadAfterAsync(_payloadsAtRelease[index - 1]).WaitAsync(s_guard);
                }
            }

            if (_minPayloads[index] > 0)
            {
                await PayloadAfterAsync(_minPayloads[index] - 1).WaitAsync(s_guard);
            }

            lock (_sync)
            {
                _payloadsAtRelease[index] = _payloads;
            }
        }

        public bool Fails(string schema, string body) => IsStep(FailingStep, schema, body);

        public bool Stalls(string schema, string body) => IsStep(StalledStep, schema, body);

        public void StepCancelled(string schema, string body)
        {
            lock (_sync)
            {
                foreach (var id in StepId().Matches(body).Select(m => m.Groups[1].Value))
                {
                    _cancelled.Add($"{schema}:{id}");
                }
            }
        }

        private static bool IsStep(string? step, string schema, string body)
            => step is not null && StepId().Matches(body).Any(m => $"{schema}:{m.Groups[1].Value}" == step);

        public void AssertEveryStepRan()
        {
            for (var i = 0; i < _order.Length; i++)
            {
                if (!_done[i].Task.IsCompleted)
                {
                    throw new InvalidOperationException($"The step {_order[i]} of the release order never ran.");
                }
            }
        }

        public void StepDone(string schema, int id)
        {
            lock (_sync)
            {
                _events.Add($"done {schema}:{id}");
            }

            var index = Array.IndexOf(_order, schema + ":" + id);

            if (index >= 0)
            {
                _done[index].TrySetResult();
            }
        }

        public void PayloadWritten()
        {
            lock (_sync)
            {
                _payloads++;

                foreach (var (count, signal) in _payloadWaiters)
                {
                    if (_payloads > count)
                    {
                        signal.TrySetResult();
                    }
                }
            }
        }

        private Task PayloadAfterAsync(int count)
        {
            lock (_sync)
            {
                if (_payloads > count)
                {
                    return Task.CompletedTask;
                }

                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _payloadWaiters.Add((count, signal));
                return signal.Task;
            }
        }
    }

    [GeneratedRegex(@"Op_[0-9a-f]+_(\d+)")]
    private static partial Regex StepId();

    // Answers the requests like the source does, except for the stalled step, which waits until the gateway cancels it.
    private sealed class StallingHandler(ResponseGate gate, string schema) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await request.Content!.LoadIntoBufferAsync();
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            await gate.EnterAsync(schema, body);

            if (gate.Stalls(schema, body))
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    gate.StepCancelled(schema, body);
                    throw;
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    // Records the steps ("schema:id") whose request failed in transport.
    private sealed class TransportErrorListener(List<string> steps) : FusionExecutionDiagnosticEventListener
    {
        public override void SourceSchemaTransportError(
            OperationPlanContext context,
            ExecutionNode node,
            string schemaName,
            Exception error)
        {
            lock (steps)
            {
                steps.Add($"{schemaName}:{node.Id}");
            }
        }
    }

    private sealed class StepDoneListener(ResponseGate gate) : FusionExecutionDiagnosticEventListener
    {
        public override IDisposable ExecuteOperationNode(
            OperationPlanContext context,
            OperationExecutionNode node,
            string schemaName)
            => new Scope(() => gate.StepDone(schemaName, node.Id));

        public override IDisposable ExecuteOperationBatchNode(
            OperationPlanContext context,
            OperationBatchExecutionNode node,
            string schemaName)
            => new Scope(() =>
            {
                gate.StepDone(schemaName, node.Id);

                foreach (var operation in node.Operations)
                {
                    gate.StepDone(schemaName, operation.Id);
                }
            });

        private sealed class Scope(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    // Counts the payloads the gateway writes to the client. Every payload of a stream has one hasNext.
    private sealed class PayloadCountingStream(Stream inner, ResponseGate gate) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Count(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Count(buffer.Span);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private void Count(ReadOnlySpan<byte> written)
        {
            var marker = "\"hasNext\""u8;

            while (written.IndexOf(marker) is var index and >= 0)
            {
                gate.PayloadWritten();
                written = written[(index + marker.Length)..];
            }
        }
    }

    private static async Task<string> PostAsync(Gateway gateway, string body, string accept)
    {
        using var client = gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/graphql");
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(DropExtensions);
        return $"{(int)response.StatusCode} {response.Content.Headers.ContentType}"
            + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string DropExtensions(string line)
    {
        var node = JsonNode.Parse(line)!.AsObject();
        node.Remove("extensions");
        return node.ToJsonString();
    }

    private static async Task<string> ReadBodyAsync(Gateway gateway, string query)
    {
        using var client = gateway.CreateClient();
        using var content = new StringContent(
            new JsonObject { ["query"] = query }.ToJsonString(),
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            "http://localhost:5000/graphql",
            content,
            TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static void MatchPayloads(Run run, string? postFix = null)
    {
        Assert.Empty(run.Problems);

        var snapshot = Snapshot.Create(postFix);

        for (var i = 0; i < run.Payloads.Count; i++)
        {
            snapshot.Add(run.Payloads[i], $"Payload {i + 1}", "json");
        }

        snapshot.Add(run.RequestSummary, "Requests", "text");
        snapshot.MatchMarkdownSnapshot();
    }

    private static void MatchPayloadShapes(Run run)
    {
        Assert.Empty(run.Problems);

        var snapshot = Snapshot.Create();

        for (var i = 0; i < run.Payloads.Count; i++)
        {
            snapshot.Add(ToShape(run.Payloads[i]), $"Payload {i + 1}", "json");
        }

        snapshot.Add(run.RequestSummary, "Requests", "text");
        snapshot.MatchMarkdownSnapshot();
    }

    private static void MatchRun(TwinRun run, string? postFix = null, bool compareValues = true)
    {
        run.AssertMatchesTwin(compareValues);

        var snapshot = Snapshot.Create(postFix);

        for (var i = 0; i < run.Deferred.Payloads.Count; i++)
        {
            snapshot.Add(ToShape(run.Deferred.Payloads[i]), $"Payload {i + 1}", "json");
        }

        snapshot.Add(
            $"deferred: {run.Deferred.RequestSummary}" + Environment.NewLine
            + $"plain twin: {run.Twin.RequestSummary}",
            "Requests",
            "text");
        snapshot.MatchMarkdownSnapshot();
    }

    private async Task<TwinRun> RunWithTwinAsync(
        Gateway gateway,
        string query,
        Dictionary<string, object?> variables)
    {
        var deferred = await RunDeferredAsync(gateway, query, variables);

        _gate?.Enabled = false;

        var twinQuery = ToPlainTwin(query);
        var twinVariables = variables
            .Where(t => twinQuery.Contains("$" + t.Key, StringComparison.Ordinal))
            .ToDictionary(t => t.Key, t => t.Value);
        var twin = await RunAsync(gateway, twinQuery, twinVariables);
        return new TwinRun(deferred, twin);
    }

    private async Task<Run> RunDeferredAsync(
        Gateway gateway,
        string query,
        Dictionary<string, object?> variables)
    {
        var run = await RunAsync(gateway, query, variables);
        _gate?.AssertEveryStepRan();
        return run;
    }

    private static async Task<Run> RunAsync(
        Gateway gateway,
        string query,
        Dictionary<string, object?> variables)
    {
        var before = CountRequests(gateway);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var result = await client.PostAsync(
            new OperationRequest(query, variables: variables),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result);
        var after = CountRequests(gateway);

        var requests = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (name, count) in after)
        {
            var difference = count - before.GetValueOrDefault(name);

            if (difference > 0)
            {
                requests[name] = difference;
            }
        }

        return new Run(payloads, requests);
    }

    private static Dictionary<string, int> CountRequests(Gateway gateway)
        => gateway.Interactions.ToDictionary(
            t => t.Key,
            t => t.Value.Values.Count(i => i.Request is not null),
            StringComparer.Ordinal);

    private static async Task<List<string>> ReadPayloadsAsync(GraphQLHttpResponse response)
    {
        var body = await response.HttpResponseMessage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var payloads = new List<string>();

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = JsonNode.Parse(line)!.AsObject();
            payload.Remove("extensions");
            payloads.Add(payload.ToJsonString());
        }

        return payloads;
    }

    // Removes every @defer directive and every variable definition that has no use left.
    private static string ToPlainTwin(string query)
    {
        var plain = DeferDirective().Replace(query, string.Empty);
        var header = QueryHeader().Match(plain);

        if (!header.Success)
        {
            return plain;
        }

        var body = plain[header.Length..];
        var kept = VariableDefinition().Matches(header.Groups["variables"].Value)
            .Where(m => body.Contains("$" + m.Groups["name"].Value, StringComparison.Ordinal))
            .Select(m => m.Value.Trim().TrimEnd(','))
            .ToList();

        return kept.Count == 0
            ? "query" + body
            : "query(" + string.Join(", ", kept) + ")" + body;
    }

    [GeneratedRegex(@"\s*@defer(\([^)]*\))?")]
    private static partial Regex DeferDirective();

    [GeneratedRegex(@"query\s*\((?<variables>[^)]*)\)")]
    private static partial Regex QueryHeader();

    [GeneratedRegex(@"\$(?<name>\w+)\s*:\s*[^,$]+")]
    private static partial Regex VariableDefinition();

    private sealed record Run(List<string> Payloads, SortedDictionary<string, int> Requests)
    {
        public JsonNode Data
        {
            get
            {
                try
                {
                    return MergeIncrementalPayloads(Payloads);
                }
                catch (NullReferenceException ex)
                {
                    throw new InvalidOperationException(
                        "The payloads cannot be merged: " + string.Join(Environment.NewLine, Payloads),
                        ex);
                }
            }
        }

        public string Errors => string.Join(
            "|",
            CollectErrors(Payloads).OrderBy(t => t, StringComparer.Ordinal));

        public int IndexOfPending(string label)
            => Payloads.FindIndex(
                t => JsonNode.Parse(t)!["pending"]?.AsArray()
                    .Any(p => p!["label"]?.GetValue<string>() == label) is true);

        public int IndexOfCompleted(string label)
        {
            var ids = new Dictionary<string, string>();

            for (var i = 0; i < Payloads.Count; i++)
            {
                var payload = JsonNode.Parse(Payloads[i])!.AsObject();

                foreach (var pending in payload["pending"]?.AsArray() ?? [])
                {
                    ids[pending!["id"]!.GetValue<string>()] = pending["label"]?.GetValue<string>() ?? string.Empty;
                }

                foreach (var completed in payload["completed"]?.AsArray() ?? [])
                {
                    if (ids[completed!["id"]!.GetValue<string>()] == label)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        public int PendingCount => Payloads
            .Select(t => JsonNode.Parse(t)!.AsObject())
            .Sum(t => t["pending"]?.AsArray().Count ?? 0);

        public IReadOnlyList<string> Positions => CollectDeliveredPositions(Payloads);

        public IReadOnlyList<string> Problems => ReadDelivery(Payloads);

        public string RequestSummary => string.Join(", ", Requests.Select(t => $"{t.Key}={t.Value}"));
    }

    private sealed record TwinRun(Run Deferred, Run Twin)
    {
        public void AssertMatchesTwin(bool compareValues)
        {
            JsonNode? twinData;
            JsonNode? deferredData;

            try
            {
                twinData = compareValues ? Twin.Data : ToShape(Twin.Data);
                deferredData = compareValues ? Deferred.Data : ToShape(Deferred.Data);
            }
            catch (NullReferenceException ex)
            {
                throw new InvalidOperationException(
                    "Twin: " + string.Join(Environment.NewLine, Twin.Payloads) + Environment.NewLine
                    + "Deferred: " + string.Join(Environment.NewLine, Deferred.Payloads),
                    ex);
            }

            Assert.True(
                Canonical(twinData) == Canonical(deferredData),
                "Expected " + Canonical(Twin.Data) + Environment.NewLine
                + "Actual   " + string.Join(Environment.NewLine, Deferred.Payloads));
            Assert.Equal(Twin.Errors, Deferred.Errors);
            Assert.Empty(Deferred.Problems);
        }
    }

    // The payload without the values of the scalars, which the mocked source schemas generate.
    private static string ToShape(string payload)
    {
        var node = JsonNode.Parse(payload)!.AsObject();

        if (node["data"] is { } data)
        {
            node["data"] = ToShape(data);
        }

        foreach (var incremental in node["incremental"]?.AsArray() ?? [])
        {
            incremental!["data"] = ToShape(incremental["data"]);
        }

        return node.ToJsonString();
    }

    private static JsonNode? ToShape(JsonNode? node)
        => node switch
        {
            null => null,
            JsonObject obj => new JsonObject(
                obj.Select(t => KeyValuePair.Create(t.Key, ToShape(t.Value)))),
            JsonArray array => new JsonArray(array.Select(ToShape).ToArray()),
            JsonValue value when value.GetValueKind() is JsonValueKind.String => JsonValue.Create("s"),
            JsonValue value when value.GetValueKind() is JsonValueKind.Number => JsonValue.Create(0),
            _ => node.DeepClone()
        };

    // The order of the properties of a merged result depends on the order of the payloads.
    private static string Canonical(JsonNode? node)
        => node switch
        {
            null => "null",
            JsonObject obj => "{"
                + string.Join(
                    ",",
                    obj.OrderBy(t => t.Key, StringComparer.Ordinal)
                        .Select(t => "\"" + t.Key + "\":" + Canonical(t.Value)))
                + "}",
            JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
            _ => node.ToJsonString()
        };

    // Reads the payloads in the order they were sent and lists every break of the delivery rules:
    // every id is announced once and completed exactly once, an entry refers to an id that an earlier or the
    // same payload announced and that is still open, an announced path holds an object, hasNext is false only
    // on the last payload, a single payload carries neither pending nor hasNext, an incremental entry targets
    // an object that exists and never carries a field the object already has, no payload sends data for an id that it
    // completes with errors, a payload after the first that announces an id completes, without errors and in the
    // same payload, another id whose path encloses the announced path, no two ids share a label at one path, and the
    // last payload carries hasNext false.
    private static List<string> ReadDelivery(List<string> payloads)
    {
        var problems = new List<string>();
        var pending = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        var labelledPlaces = new Dictionary<string, string>(StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? data = null;

        for (var i = 0; i < payloads.Count; i++)
        {
            var payload = JsonNode.Parse(payloads[i])!.AsObject();
            var number = i + 1;
            var isLast = i == payloads.Count - 1;
            var hasNext = payload["hasNext"]?.GetValue<bool>() is true;
            var incremental = payload["incremental"]?.AsArray() ?? [];
            var announced = payload["pending"]?.AsArray();
            var done = payload["completed"]?.AsArray() ?? [];

            if (i == 0)
            {
                data = payload["data"]?.DeepClone();
            }
            else if (payload.ContainsKey("data"))
            {
                problems.Add($"payload {number} carries data");
            }

            if (hasNext == isLast)
            {
                problems.Add($"payload {number} of {payloads.Count} has hasNext {hasNext}");
            }

            if (isLast && payloads.Count > 1 && payload["hasNext"] is not JsonValue)
            {
                problems.Add($"payload {number} is the last payload and has no hasNext");
            }

            var failedIds = done
                .Where(t => t!["errors"] is JsonArray { Count: > 0 })
                .Select(t => t!["id"]!.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);

            if (announced is { Count: 0 })
            {
                problems.Add($"payload {number} has an empty pending list");
            }

            if (payloads.Count == 1 && (announced is not null || payload.ContainsKey("hasNext")))
            {
                problems.Add("the only payload carries pending or hasNext");
            }

            if (i > 0 && hasNext && incremental.Count == 0 && done.Count == 0 && announced is null)
            {
                problems.Add($"payload {number} carries only hasNext");
            }

            var announcedPaths = new List<(string Id, JsonArray Path)>();

            foreach (var entry in announced ?? [])
            {
                var id = entry!["id"]!.GetValue<string>();

                if (!pending.TryAdd(id, entry["path"]!.AsArray()))
                {
                    problems.Add($"payload {number} announces id {id} again");
                }

                if (entry["label"] is JsonValue label
                    && !labelledPlaces.TryAdd(label.GetValue<string>() + " at " + entry["path"]!.ToJsonString(), id))
                {
                    problems.Add($"payload {number} announces id {id} with a label and path that another id has");
                }

                announcedPaths.Add((id, entry["path"]!.AsArray()));
            }

            foreach (var entry in incremental)
            {
                var id = entry!["id"]!.GetValue<string>();

                if (!pending.TryGetValue(id, out var path) || completed.Contains(id))
                {
                    problems.Add($"payload {number} has data for id {id}, which is not pending");
                    continue;
                }

                if (failedIds.Contains(id))
                {
                    problems.Add($"payload {number} has data for id {id}, which it completes with errors");
                }

                var target = data;
                var position = string.Empty;

                foreach (var segment in path.Concat(entry["subPath"]?.AsArray() ?? []))
                {
                    var value = segment!.GetValue<JsonElement>();
                    position += "/" + value;
                    target = value.ValueKind is JsonValueKind.Number
                        ? (target as JsonArray)?[value.GetInt32()]
                        : (target as JsonObject)?[value.GetString()!];
                }

                if (target is not JsonObject targetObject || entry["data"] is not JsonObject source)
                {
                    problems.Add($"payload {number} has data for {position}, which is not an object");
                    continue;
                }

                MergeStrict(targetObject, source, position, number, problems);
            }

            foreach (var (id, path) in announcedPaths)
            {
                var target = data;

                foreach (var segment in path)
                {
                    var value = segment!.GetValue<JsonElement>();
                    target = value.ValueKind is JsonValueKind.Number
                        ? (target is JsonArray array && value.GetInt32() < array.Count ? array[value.GetInt32()] : null)
                        : (target as JsonObject)?[value.GetString()!];
                }

                if (target is not JsonObject)
                {
                    problems.Add($"payload {number} announces id {id} at {path.ToJsonString()}, which is no object");
                }
            }

            foreach (var entry in done)
            {
                var id = entry!["id"]!.GetValue<string>();

                if (!pending.ContainsKey(id) || !completed.Add(id))
                {
                    problems.Add($"payload {number} completes id {id}, which is not pending");
                }
            }

            if (i > 0)
            {
                foreach (var (id, path) in announcedPaths)
                {
                    var completesEnclosing = done
                        .Select(t => t!["id"]!.GetValue<string>())
                        .Any(other => other != id
                            && !failedIds.Contains(other)
                            && pending.TryGetValue(other, out var otherPath)
                            && IsEnclosingPath(otherPath, path));

                    if (!completesEnclosing)
                    {
                        problems.Add(
                            $"payload {number} announces id {id} and completes no enclosing id without errors");
                    }
                }
            }
        }

        foreach (var id in pending.Keys.Where(id => !completed.Contains(id)))
        {
            problems.Add($"id {id} is never completed");
        }

        return problems;
    }

    // A path encloses another path when it is the same or a prefix of it.
    private static bool IsEnclosingPath(JsonArray outer, JsonArray inner)
    {
        if (outer.Count > inner.Count)
        {
            return false;
        }

        for (var i = 0; i < outer.Count; i++)
        {
            if (outer[i]!.ToJsonString() != inner[i]!.ToJsonString())
            {
                return false;
            }
        }

        return true;
    }

    private static void MergeStrict(
        JsonObject target,
        JsonObject source,
        string position,
        int number,
        List<string> problems)
    {
        foreach (var (name, value) in source)
        {
            if (!target.ContainsKey(name))
            {
                target[name] = value?.DeepClone();
                continue;
            }

            problems.Add($"payload {number} sends {position}/{name} again");

            // The values are still merged the lenient way so that later entries find their target.
            if (value is JsonObject sourceObject && target[name] is JsonObject targetObject)
            {
                MergeInto(targetObject, sourceObject);
            }
            else if (value is JsonArray sourceArray && target[name] is JsonArray targetArray)
            {
                MergeInto(
                    new JsonObject { [name] = targetArray.DeepClone() },
                    new JsonObject { [name] = sourceArray.DeepClone() });
            }
        }
    }

    private static IEnumerable<string> CollectErrors(List<string> payloads)
    {
        foreach (var payload in payloads.Select(t => JsonNode.Parse(t)!.AsObject()))
        {
            foreach (var error in payload["errors"]?.AsArray() ?? [])
            {
                yield return error!.ToJsonString();
            }

            foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
            {
                foreach (var error in incremental!["errors"]?.AsArray() ?? [])
                {
                    yield return error!.ToJsonString();
                }
            }

            foreach (var completed in payload["completed"]?.AsArray() ?? [])
            {
                foreach (var error in completed!["errors"]?.AsArray() ?? [])
                {
                    yield return error!.ToJsonString();
                }
            }
        }
    }

    // Every scalar leaf that a payload sends, as the path from the root of the result.
    private static List<string> CollectDeliveredPositions(List<string> payloads)
    {
        var positions = new List<string>();
        var pendingPaths = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var payload in payloads.Select(t => JsonNode.Parse(t)!.AsObject()))
        {
            if (payload["data"] is { } initial)
            {
                CollectLeaves(initial, string.Empty, positions);
            }

            foreach (var pending in payload["pending"]?.AsArray() ?? [])
            {
                pendingPaths[pending!["id"]!.GetValue<string>()] = JoinPath(pending["path"]!.AsArray());
            }

            foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
            {
                var path = pendingPaths[incremental!["id"]!.GetValue<string>()]
                    + JoinPath(incremental["subPath"]?.AsArray() ?? []);
                CollectLeaves(incremental["data"]!, path, positions);
            }
        }

        return positions;

        static string JoinPath(JsonArray segments)
            => string.Concat(segments.Select(t => "/" + t!.ToJsonString().Trim('"')));

        static void CollectLeaves(JsonNode node, string path, List<string> positions)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, value) in obj)
                    {
                        CollectLeaves(value!, path + "/" + name, positions);
                    }
                    break;

                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        CollectLeaves(array[i]!, path + "/" + i, positions);
                    }
                    break;

                default:
                    positions.Add(path);
                    break;
            }
        }
    }

    private static JsonNode MergeIncrementalPayloads(List<string> payloads)
    {
        JsonNode? data = null;
        var pendingPaths = new Dictionary<string, JsonArray>(StringComparer.Ordinal);

        foreach (var payload in payloads)
        {
            var response = JsonNode.Parse(payload)!.AsObject();

            if (response["data"] is { } initial)
            {
                data = initial.DeepClone();
            }

            foreach (var pending in response["pending"]?.AsArray() ?? [])
            {
                pendingPaths[pending!["id"]!.GetValue<string>()] = pending["path"]!.AsArray();
            }

            var entries = (response["incremental"]?.AsArray() ?? [])
                .Select(incremental => (
                    Incremental: incremental!,
                    Segments: pendingPaths[incremental!["id"]!.GetValue<string>()]
                        .Concat(incremental["subPath"]?.AsArray() ?? [])
                        .ToList()))
                .OrderBy(entry => entry.Segments.Count);

            foreach (var (incremental, segments) in entries)
            {
                var target = data!;

                foreach (var segment in segments)
                {
                    var value = segment!.GetValue<JsonElement>();
                    target = value.ValueKind == JsonValueKind.Number
                        ? target[value.GetInt32()]!
                        : target[value.GetString()!]!;
                }

                MergeInto(target.AsObject(), incremental["data"]!.AsObject());
            }
        }

        return data!;
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source.ToArray())
        {
            if (value is JsonObject sourceObject && target[name] is JsonObject targetObject)
            {
                MergeInto(targetObject, sourceObject);
            }
            else if (value is JsonArray sourceArray && target[name] is JsonArray targetArray)
            {
                for (var i = 0; i < sourceArray.Count; i++)
                {
                    if (sourceArray[i] is JsonObject sourceItem && targetArray[i] is JsonObject targetItem)
                    {
                        MergeInto(targetItem, sourceItem);
                    }
                    else
                    {
                        targetArray[i] = sourceArray[i]?.DeepClone();
                    }
                }
            }
            else
            {
                target[name] = value?.DeepClone();
            }
        }
    }
}
