using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Transport.Http;
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
        // the response of the shared outer lookup reaches the gateway first, then each nested lookup answers.
        var gateway = await CreateGatewayAsync(
            ["B:2>", "C:3", "C:5"],
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
        Assert.True(run.Deferred.Requests.Values.Sum() <= 4, run.Deferred.RequestSummary);
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
        var gateway = await CreateGatewayAsync(
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
        MatchRun(run);
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
            keyStepFirst ? ["A:3", "B:2"] : ["B:2", "A:3"],
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
            innerFirst ? ["B:3", "B:2"] : ["B:2", "B:3"],
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
            ("W", """
                type Query { productById(id: ID!): Product @lookup }
                type Product @key(fields: "id") { id: ID! weight: Float }
                """),
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
    public async Task Defer_Should_ReportTheTransportError_When_AnotherStepOfTheGroupReportsAnErrorOfANullableField()
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
    public async Task Defer_Should_ReportTheTransportErrorWithoutAPath_When_TheSharedFieldWasDeliveredWithAValue()
    {
        // arrange
        var gateway = await CreateGatewayAsync(
            ["B:3>", "B:2"],
            (schema, body, text) => schema == "B" && StepId().Match(body).Groups[1].Value == "2"
                ? "this is not json"
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
        MatchPayloads(run);
    }

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

    private const string AuthorBioSource =
        """
        type Query { authorById(id: ID!): Author @lookup }
        type Author @key(fields: "id") { id: ID! bio: String! }
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

    private Task<Gateway> CreateProductReviewGatewayAsync(params string[] releaseOrder)
        => CreateGatewayAsync(
            releaseOrder,
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
                """));

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
    {
        var gate = _gate = new ResponseGate(releaseOrder);
        var servers = new List<(string, TestServer)>();

        foreach (var (name, sdl) in sources)
        {
            HttpMessageInvoker? invoker = null;
            var server = CreateSourceSchema(
                name,
                sdl,
                mockHttpResponse: async request =>
                {
                    await request.Content!.LoadIntoBufferAsync();
                    var body = await request.Content.ReadAsStringAsync();
                    await gate.EnterAsync(name, body);

                    if (name == failingSchema)
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
                "Expected " + Twin.Data.ToJsonString() + Environment.NewLine
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
    // same payload, another id whose path encloses the announced path, and the last payload carries hasNext false.
    private static List<string> ReadDelivery(List<string> payloads)
    {
        var problems = new List<string>();
        var pending = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
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
