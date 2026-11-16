using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.Tracing;
using System.Text.RegularExpressions;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Nodes.Serialization;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Planning;

public partial class SinglePlanDeferPlannerTests : FusionTestBase
{
    [Fact]
    public void Plan_Should_MarkTheLookupOfEachGroup_When_TwoSwitchesSelectFieldsOfOtherSchemas()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "price") { price }
                    ... @defer(if: $b, label: "reviews") { reviews { body } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkALookupBackIntoTheSameSchema_When_TheDeferredFieldLivesOnTheParentSchema()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                product(id: ID!): Product @lookup
            }

            type Product @key(fields: "id") {
                id: ID!
                name: String!
                price: Float!
            }
            """,
            ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "price") { price }
                    ... @defer(if: $b, label: "reviews") { reviews { body } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanThePlainAndTheDeferredCopy_When_AFieldIsConditionalAndDeferred()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") { name price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanFourCopiesOfTheInnerFragment_When_SwitchesAreNested()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);

        // act
        var result = Plan(
            schema,
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
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkTheLookup_When_TheAnchorIsInsideAList()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                products: [Product!]!
                product(id: ID!): Product @lookup
            }

            type Product @key(fields: "id") {
                id: ID!
                name: String!
            }
            """,
            PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                products {
                    name
                    ... @defer(label: "price") { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkTheLookup_When_TheAnchorIsBelowAMutationResult()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                product(id: ID!): Product @lookup
            }

            type Mutation {
                createProduct(name: String!): Product!
            }

            type Product @key(fields: "id") {
                id: ID!
                name: String!
            }
            """,
            PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            mutation {
                createProduct(name: "Shoe") {
                    name
                    ... @defer(label: "price") { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanTheFragmentInline_When_TheAnchorTypeHasNoLookup()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                user(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                name: String!
                profile: Profile!
            }

            type Profile {
                bio: String!
                avatar: String!
            }
            """);

        // act
        var result = Plan(
            schema,
            """
            {
                user(id: "1") {
                    name
                    profile { ... @defer(label: "avatar") { avatar } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanOneMarkedStep_When_AnOuterFragmentOnlyHoldsANestedFragment()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "outer") {
                        ... @defer(label: "inner") { price }
                    }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_ShowWhichStepProvidesTheNestedKey_When_AFieldIsConditionalAndDeferredTwice()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                user(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                friends: [User!]!
            }
            """,
            """
            # name: b
            type Query {
                userById(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                name: String!
            }
            """);

        // act
        var result = Plan(
            schema,
            """
            query($x: Boolean!) {
                user(id: "1") {
                    friends @include(if: $x) { id }
                    ... @defer { friends { ... @defer { name } } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_FetchOnlyTheMissingFields_When_TheDeferredFragmentRepeatsAFieldOfTheParent()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                user(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                profile: Profile! @shareable
            }

            type Profile {
                bio: String!
            }
            """,
            """
            # name: b
            type Query {
                userById(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                total: Int!
                profile: Profile! @shareable
            }

            type Profile {
                avatar: String!
            }
            """);

        // act
        var result = Plan(
            schema,
            """
            {
                user(id: "1") {
                    profile { bio }
                    ... @defer(label: "L") { profile { bio avatar } total }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkARootStep_When_TheFragmentSitsAtTheQueryRoot()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") { name }
                ... @defer(label: "root") { other: product(id: "2") { name } }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkEveryStepOfTheGroup_When_TheDeferredFragmentSpansTwoSchemas()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "details") { price reviews { body } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepSiblingGroupsApart_When_TheyFetchTheSameFields()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { price }
                    ... @defer(label: "b") { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MergeIntoOneMarkedStep_When_UnlabelledSiblingsFetchTheSameField()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { price }
                    ... @defer { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanTheFieldsOfBothSiblings_When_UnlabelledSiblingsHaveDifferentBodies()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { price }
                    ... @defer { price rating }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepTwoSteps_When_ALabelledAndAnUnlabelledSiblingFetchTheSameField()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "a") { price }
                    ... @defer { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepTwoSteps_When_UnlabelledSiblingsHaveDifferentSwitches()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a) { price }
                    ... @defer(if: $b) { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepTwoSteps_When_UnlabelledFragmentsSitAtDifferentPaths()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, AuthorAndEditorSchema, AuthorBioAndNickSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    author { ... @defer { bio } }
                    editor { ... @defer { bio } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MergeTheRequirementStepsOfBothSiblings_When_UnlabelledSiblingsFetchTheSameRequireField()
    {
        // arrange
        var schema = ComposeSchema(CatalogSchema, PriceListSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                products {
                    upc
                    ... @defer { eur }
                    ... @defer { eur }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MergeTheOuterSteps_When_UnlabelledSiblingsHoldTheSameNestedFragment()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, AuthorAndEditorSchema, AuthorBioAndNickSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer { author { ... @defer { bio } } }
                    ... @defer { author { ... @defer { bio } } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_NotBatchAMarkedStepWithAnUnmarkedStep_When_BothTargetOneSchema()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    rating
                    ... @defer(label: "price") { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanWithoutDefer_When_ADeferredFieldNeedsTheParentPath()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            """
            # name: b
            type Query {
                productById(id: ID!): Product @lookup
            }

            type Product @key(fields: "id") {
                id: ID!
                details: Details! @shareable
            }

            type Details {
                size: Int!
            }
            """,
            """
            # name: c
            type Query {
                productByIdForDetails(id: ID!): Product @lookup
            }

            type Product @key(fields: "id") {
                id: ID!
                details: Details! @shareable
            }

            type Details {
                weight: Int!
            }
            """);
        const string operationText =
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "details") { details { size weight } }
                }
            }
            """;

        // act
        var result = Plan(schema, operationText);

        // assert
        Assert.Equal(["No possible plan was found."], result.Fallbacks);
        Assert.Empty(result.Plan.DeliveryGroups);
        Assert.Equal(FormatPlanWithoutDefer(schema, operationText), Format(result.Plan));
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanLikeTheOptionIsOff_When_TheOperationIsASubscription()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                product(id: ID!): Product @lookup
            }

            type Subscription {
                onProductUpdated: Product
            }

            type Product @key(fields: "id") {
                id: ID!
                name: String!
            }
            """,
            PriceSchema);
        const string operationText =
            """
            subscription {
                onProductUpdated {
                    name
                    ... @defer(label: "price") { price }
                }
            }
            """;

        // act
        var result = Plan(schema, operationText);
        var expected = PlanOperation(schema, operationText);

        // assert
        Assert.Empty(result.Fallbacks);
        var formatter = new YamlOperationPlanFormatter();
        Assert.Equal(formatter.Format(expected), formatter.Format(result.Plan));
    }

    [Fact]
    public void Plan_Should_MarkRequirementSteps_When_DeferredFieldRequiresThirdSchema()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            ShippingCostSchema,
            WeightSchema);

        // act
        var result = Plan(
            schema,
            """
            query($v: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $v, label: "s") { shippingCost }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkRequirementSteps_When_RequirementHasArguments()
    {
        // arrange
        var schema = ComposeSchema(
            CatalogSchema,
            PriceListSchema,
            ForeignRequirementSchema);

        // act
        var result = Plan(
            schema,
            """
            query($v: Boolean!) {
                products {
                    upc
                    ... @defer(if: $v) { eur }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkRequirementStep_When_PlainCopyOfTheRequirementExists()
    {
        // arrange
        var schema = ComposeSchema(
            UserSchema,
            UserSkuSchema,
            UserTotalSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                user(id: 1) {
                    id
                    sku
                    ... @defer { sku total }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkRequirementSteps_When_DeferIsBelowAMutation()
    {
        // arrange
        var schema = ComposeSchema(
            UserSchema,
            UserSkuSchema,
            UserTotalSchema);

        // act
        var result = Plan(
            schema,
            """
            mutation {
                createUser(id: 1) {
                    soft
                    ... @defer { id sku total }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkNestedLookup_When_DeferIsInsideARequireField()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            CarrierSchema,
            WeightSchema,
            RatingSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    carrier {
                        name
                        ... @defer(label: "r") { rating }
                    }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_NotFallBack_When_RequirementsAreNested()
    {
        // arrange
        (string Name, FusionSchemaDefinition Schema, string Operation)[] shapes =
        [
            (
                "nested requirements of @require",
                ComposeSchema(ProductSchema, ShippingCostSchema, WeightSchema),
                """
                query($a: Boolean!, $b: Boolean!) {
                    product(id: "1") {
                        name
                        ... @defer(if: $a, label: "o") {
                            name
                            ... @defer(if: $b, label: "i") { shippingCost }
                        }
                    }
                }

                """),
            (
                "nested argument requirements",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                {
                    products {
                        price
                        ... @defer { eur ... @defer { usd } }
                    }
                }

                """)
        ];
        var formatter = new YamlOperationPlanFormatter();
        var snapshot = Snapshot.Create();
        var fallbacks = new List<string>();

        // act
        foreach (var (shapeName, schema, operation) in shapes)
        {
            var result = Plan(schema, operation);
            fallbacks.AddRange(result.Fallbacks);
            snapshot.Add(formatter.Format(result.Plan), shapeName, MarkdownLanguages.Yaml);
        }

        // assert
        Assert.Empty(fallbacks);
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public void Plan_Should_SkipKeyHopOfThePlainCopy_When_SwitchIsOn()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            ProductBySkuSchema,
            ProductKeysSchema);

        // act
        var result = Plan(
            schema,
            """
            query($v: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $v, label: "p") { price }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepOneStep_When_SiblingSwitchesShareALookup()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            PriceSchema,
            ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "x") { reviews { body } price }
                    ... @defer(if: $b, label: "y") { reviews { body } price }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_TakeTheKeyFromTheParentStep_When_DeferredFragmentsAreNested()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            PriceSchema,
            ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        price
                        ... @defer(label: "i") { reviews { body } }
                    }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanASiblingLookup_When_TheWrapperOfADeferredFragmentHasFieldsOfItsOwn()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            PriceSchema,
            ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query($c: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(label: "o") {
                        price
                        ... @include(if: $c) {
                            rating
                            ... @defer(label: "i") { reviews { body } }
                        }
                    }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanSiblingLookups_When_ThreeSwitchesAreNested()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            PriceSchema,
            ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!, $c: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "o") {
                        price
                        ... @defer(if: $b, label: "m") {
                            rating
                            ... @defer(if: $c, label: "i") { reviews { body } }
                        }
                    }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MarkRequirementSteps_When_DeferredGroupReadsRequirementOfSchemaTwo()
    {
        // arrange
        (string Name, FusionSchemaDefinition Schema, string Operation)[] shapes =
        [
            (
                "requirement inside the fragment",
                ComposeSchema(UserSchema, UserSkuSchema, UserTotalSchema),
                """
                { user(id: 1) { soft ... @defer { id sku total } } }
                """),
            (
                "alias inside the fragment",
                ComposeSchema(UserSchema, UserSkuSchema, UserTotalSchema),
                """
                { user(id: 1) { id sku ... @defer { s: sku total } } }
                """),
            (
                "alias outside the fragment",
                ComposeSchema(UserSchema, UserSkuSchema, UserTotalSchema),
                """
                { user(id: 1) { id k: sku ... @defer { sku total } } }
                """),
            (
                "condition on the plain copy",
                ComposeSchema(UserSchema, UserSkuSchema, UserTotalSchema),
                """
                query($c: Boolean!) { user(id: 1) { id soft sku @include(if: $c) ... @defer { sku total } } }
                """),
            (
                "switch",
                ComposeSchema(UserSchema, UserSkuSchema, UserTotalSchema),
                """
                query($v: Boolean!) { user(id: 1) { soft ... @defer(if: $v) { total } } }
                """),
            (
                "only the requiring field",
                ComposeSchema(UserSchema, UserSkuSchema, UserTotalSchema),
                """
                { user(id: 1) { soft ... @defer { total } } }
                """)
        ];
        var formatter = new YamlOperationPlanFormatter();
        var snapshot = Snapshot.Create();
        var fallbacks = new List<string>();

        // act
        foreach (var (shapeName, schema, operation) in shapes)
        {
            var result = Plan(schema, operation);
            fallbacks.AddRange(result.Fallbacks);
            snapshot.Add(formatter.Format(result.Plan), shapeName, MarkdownLanguages.Yaml);
        }

        // assert
        Assert.Empty(fallbacks);
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public void Plan_Should_MarkRequirementSteps_When_ArgumentRequirementsAreDeferred()
    {
        // arrange
        (string Name, FusionSchemaDefinition Schema, string Operation)[] shapes =
        [
            (
                "d07",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { eur usd ... @defer { both } } }
                """),
            (
                "d09",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { usd ... @defer { eur } } }
                """),
            (
                "d13",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { usd } } }
                """),
            (
                "d14",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { eur } ... @defer { usd } } }
                """),
            (
                "d15",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { eur price } } }
                """),
            (
                "d18",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { eur price usd } } }
                """),
            (
                "v16c",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { price ... @defer { eur } } }
                """),
            (
                "w01",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { product { upc ... @defer { eur } } }
                """),
            (
                "y01",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { product { upc ... @defer { eurC } } }
                """),
            (
                "y05",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { eurC eur } } }
                """),
            (
                "n1",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { netWeight } } }
                """),
            (
                "n2",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { weightC } } }
                """),
            (
                "n3",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                { products { upc ... @defer { chained } } }
                """),
            (
                "n4",
                ComposeSchema(CatalogSchema, PriceListSchema, ForeignRequirementSchema),
                """
                query($v: Boolean!) { products { upc ... @defer(if: $v) { eurC } } }
                """)
        ];
        var formatter = new YamlOperationPlanFormatter();
        var snapshot = Snapshot.Create();
        var fallbacks = new List<string>();

        // act
        foreach (var (shapeName, schema, operation) in shapes)
        {
            var result = Plan(schema, operation);
            fallbacks.AddRange(result.Fallbacks);
            snapshot.Add(formatter.Format(result.Plan), shapeName, MarkdownLanguages.Yaml);
        }

        // assert
        Assert.Empty(fallbacks);
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public void Plan_Should_MergeSiblingSwitchesIntoOneStep_When_EightSwitchesSelectTheSameField()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            query(
                $s1: Boolean!, $s2: Boolean!, $s3: Boolean!, $s4: Boolean!,
                $s5: Boolean!, $s6: Boolean!, $s7: Boolean!, $s8: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $s1, label: "l1") { price }
                    ... @defer(if: $s2, label: "l2") { price }
                    ... @defer(if: $s3, label: "l3") { price }
                    ... @defer(if: $s4, label: "l4") { price }
                    ... @defer(if: $s5, label: "l5") { price }
                    ... @defer(if: $s6, label: "l6") { price }
                    ... @defer(if: $s7, label: "l7") { price }
                    ... @defer(if: $s8, label: "l8") { price }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MergeSiblingSwitchesWithAnExactGate_When_TheirLookupsHaveOwnConditions()
    {
        // arrange
        var schema = ComposeSchema(
            ProductSchema,
            PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!, $p: Boolean!, $q: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "x") { price @include(if: $p) rating @include(if: $q) }
                    ... @defer(if: $b, label: "y") { price @include(if: $p) rating @include(if: $q) }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_FallBack_When_BudgetIsExceeded()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, ProductBySkuSchema, ProductKeysSchema);
        var options = new OperationPlannerOptions { EnableSinglePlanDefer = true, MaxExpandedNodes = 7 };
        const string operationText =
            """
            query($v: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $v, label: "p") { price }
                }
            }
            """;

        // act
        var result = Plan(schema, operationText, options, "123456789101112");

        // assert
        Assert.Equal(
            [
                "Planner guardrail exceeded for operation '123456789101112': "
                + "MaxExpandedNodesExceeded (Limit=7, Observed=8)."
            ],
            result.Fallbacks);
        Assert.Empty(result.Plan.DeliveryGroups);
        Assert.Equal(
            FormatPlanWithoutDefer(schema, operationText, options, "123456789101112"),
            Format(result.Plan));
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_RaiseNoPlanError_When_BudgetIsExceeded()
    {
        // arrange
        const string operationId = "single-plan-guardrail";
        var schema = ComposeSchema(ProductSchema, ProductBySkuSchema, ProductKeysSchema);
        var options = new OperationPlannerOptions { EnableSinglePlanDefer = true, MaxExpandedNodes = 7 };
        const string operationText =
            """
            query($v: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $v, label: "p") { price }
                }
            }
            """;
        using var listener = new PlannerEventListener(operationId);

        // act
        var result = Plan(schema, operationText, options, operationId);

        // assert
        Assert.Single(result.Fallbacks);
        Assert.Equal(
            [
                PlannerEventSource.PlanStartEventId,
                PlannerEventSource.PlanGuardrailExceededEventId,
                PlannerEventSource.PlanStartEventId,
                PlannerEventSource.PlanStopEventId
            ],
            listener.EventIds);
        Assert.Equal(FormatPlanWithoutDefer(schema, operationText, options, operationId), Format(result.Plan));
    }

    [Fact]
    public void Plan_Should_KeepLookupsApart_When_SiblingSwitchesTakeTheKeyFromDifferentSchemas()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceAuthorSchema, AuthorBioSchema, StockAuthorSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "x") { price author { bio } }
                    ... @defer(if: $b, label: "y") { stock author { bio } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepLookupsApart_When_SiblingSwitchesTakeTheKeyFromDifferentSteps()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, AuthorAndEditorSchema, AuthorBioAndNickSchema);

        // act
        var result = Plan(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "x") { price author { bio } editor { nick } }
                    ... @defer(if: $b, label: "y") { author { bio } editor { nick } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_MergeEightLookupsIntoOne_When_EightSwitchesSelectFieldsOfTwoSchemas()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query(
                $s1: Boolean!, $s2: Boolean!, $s3: Boolean!, $s4: Boolean!,
                $s5: Boolean!, $s6: Boolean!, $s7: Boolean!, $s8: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $s1, label: "l1") { reviews { body } price }
                    ... @defer(if: $s2, label: "l2") { reviews { body } price }
                    ... @defer(if: $s3, label: "l3") { reviews { body } price }
                    ... @defer(if: $s4, label: "l4") { reviews { body } price }
                    ... @defer(if: $s5, label: "l5") { reviews { body } price }
                    ... @defer(if: $s6, label: "l6") { reviews { body } price }
                    ... @defer(if: $s7, label: "l7") { reviews { body } price }
                    ... @defer(if: $s8, label: "l8") { reviews { body } price }
                }
            }

            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_EqualThePlanWithoutDefer_When_LoweringRemovesEveryDeferredFragment()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);
        const string operationText =
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @skip(if: $a) { reviews { body } price }
                    ... @skip(if: $b) { reviews { body } price }
                    ... @defer(label: "n") { name }
                }
            }
            """;
        const string operationTextWithoutDefer =
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @skip(if: $a) { reviews { body } price }
                    ... @skip(if: $b) { reviews { body } price }
                }
            }
            """;

        // act
        var result = Plan(schema, operationText);
        var planWithoutDefer = Plan(schema, operationTextWithoutDefer);

        // assert
        Assert.Empty(result.Fallbacks);
        Assert.Equal(Format(planWithoutDefer.Plan), Format(result.Plan));
    }

    [Fact]
    public void Plan_Should_EqualThePlanWithoutDefer_When_TheFragmentSelectsOnlyTypename()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);
        const string operationText =
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "t") { __typename }
                }
            }
            """;
        const string operationTextWithoutDefer =
            """
            {
                product(id: "1") {
                    name
                    __typename
                }
            }
            """;

        // act
        var result = Plan(schema, operationText);
        var planWithoutDefer = Plan(schema, operationTextWithoutDefer);

        // assert
        Assert.Empty(result.Fallbacks);
        Assert.Empty(result.Plan.DeliveryGroups);
        Assert.All(result.Plan.AllNodes.OfType<OperationExecutionNode>(), static n => Assert.Empty(n.DeliveryGroups));
        Assert.Equal(Format(planWithoutDefer.Plan), Format(result.Plan));
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepTheWrapperFieldsInTheGroupStep_When_TheWrapperIsTypedWithTheLookupType()
    {
        // arrange
        var schema = ComposeSchema(ItemSchema, PricedBookSchema, BookReviewsSchema);

        // act
        var result = Plan(
            schema,
            """
            query($c: Boolean!) {
                item(id: "1") {
                    name
                    ... on Priced @defer(label: "o") {
                        ... on Book @include(if: $c) {
                            rating
                            ... @defer(label: "i") { reviews { body } }
                        }
                    }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_FallBackWithTheException_When_ARootFragmentSelectsANodeFieldAndAnotherField()
    {
        // arrange
        var schema = ComposeSchema(NodeProductSchema, NodePriceSchema);
        const string operationText =
            """
            {
                product(id: "1") { name }
                ... @defer(label: "n") {
                    productById(id: "2") { price }
                    node(id: "1") { id }
                }
            }
            """;

        // act
        var result = Plan(schema, operationText);

        // assert
        Assert.Equal(
            [
                "The given key '{\n  productById(id: \"2\") {\n    price\n  }\n}' was not present in the dictionary."
            ],
            result.Fallbacks);
        Assert.Equal(FormatPlanWithoutDefer(schema, operationText), Format(result.Plan));
    }

    [Fact]
    public void ValidateDeliveryGroupNodes_Should_Succeed_When_TheNodesAgreeWithTheGroups()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema, ReviewsSchema);
        const string operationText =
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    name
                    ... @defer(if: $a, label: "price") { price }
                    ... @defer(if: $b, label: "reviews") { reviews { body } }
                }
            }
            """;
        var plan = Plan(schema, operationText).Plan;
        var partitioning = Partition(schema, operationText);

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupNodes(
            plan,
            partitioning.AllDeliveryGroups,
            partitioning,
            out var cause);

        // assert
        Assert.True(valid);
        Assert.Null(cause);
    }

    [Fact]
    public void ValidateDeliveryGroupNodes_Should_Fail_When_ANodeDependsOnANodeOfAnotherGroup()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
                user(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                friends: [User!]!
            }
            """,
            """
            # name: b
            type Query {
                userById(id: ID!): User @lookup
            }

            type User @key(fields: "id") {
                id: ID!
                name: String!
            }
            """);
        const string operationText =
            """
            query {
                user(id: "1") {
                    ... @defer { friends { id ... @defer { name } } }
                }
            }
            """;
        var plan = Plan(schema, operationText).Plan;
        var partitioning = Partition(schema, operationText);
        var groups = partitioning.AllDeliveryGroups;
        ImmutableArray<DeliveryGroup> unrelatedGroups = [groups[0], groups[1] with { Parent = null }];

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupNodes(plan, unrelatedGroups, partitioning, out var cause);

        // assert
        Assert.False(valid);
        Assert.Equal("Node 3 depends on node 2 of another delivery group.", cause);
    }

    [Fact]
    public void ValidateDeliveryGroupNodes_Should_Fail_When_ADeliveryGroupHasNoNode()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);
        var plan = Plan(schema, """{ product(id: "1") { name price } }""").Plan;
        var partitioning = Partition(
            schema,
            """{ product(id: "1") { name ... @defer(label: "price") { price } } }""");

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupNodes(
            plan,
            partitioning.AllDeliveryGroups,
            partitioning,
            out var cause);

        // assert
        Assert.False(valid);
        Assert.Equal("Delivery group 0 has no step.", cause);
    }

    [Fact]
    public void ValidateDeliveryGroupNodes_Should_Succeed_When_TheRootGroupHoldsOnlyIntrospectionFields()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);
        const string operationText =
            """
            {
                product(id: "1") { name }
                ... @defer(label: "types") { __type(name: "Product") { name } }
            }
            """;
        var plan = Plan(schema, operationText).Plan;
        var partitioning = Partition(schema, operationText);

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupNodes(
            plan,
            partitioning.AllDeliveryGroups,
            partitioning,
            out var cause);

        // assert
        Assert.True(valid);
        Assert.Null(cause);
    }

    [Fact]
    public void ValidateDeliveryGroupDependencies_Should_Fail_When_AnUnmarkedStepDependsOnAMarkedStep()
    {
        // arrange
        var steps = CreateSteps(
            (1, [0], [2]),
            (2, [], []));
        var groups = CreateGroups(-1);

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupDependencies(steps, groups, out var cause);

        // assert
        Assert.False(valid);
        Assert.Equal("Step 2 depends on step 1 of another delivery group.", cause);
    }

    [Fact]
    public void ValidateDeliveryGroupDependencies_Should_Fail_When_AParentGroupStepDependsOnANestedGroupStep()
    {
        // arrange
        var steps = CreateSteps(
            (1, [1], [2]),
            (2, [0], []));
        var groups = CreateGroups(-1, 0);

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupDependencies(steps, groups, out var cause);

        // assert
        Assert.False(valid);
        Assert.Equal("Step 2 depends on step 1 of another delivery group.", cause);
    }

    [Fact]
    public void ValidateDeliveryGroupDependencies_Should_Succeed_When_ANestedGroupStepDependsOnAParentGroupStep()
    {
        // arrange
        var steps = CreateSteps(
            (1, [0], [2, 3]),
            (2, [1], []),
            (3, [0], []));
        var groups = CreateGroups(-1, 0);

        // act
        var valid = OperationPlanner.TryValidateDeliveryGroupDependencies(steps, groups, out var cause);

        // assert
        Assert.True(valid);
        Assert.Null(cause);
    }

    [Fact]
    public void DeliveryGroupMaxCount_Should_EqualTheClaimantMaskOfTheResultDocument()
    {
        // arrange
        const int claimantMask = CompositeResultDocument.PropertyMarks.ClaimantMask;

        // assert
        Assert.Equal(claimantMask, DeliveryGroup.MaxCount);
    }

    [Fact]
    public void ValidateCompiledDeliveryGroups_Should_Fail_When_TheLabelOfAGroupDiffers()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);
        var planner = CreatePlanner(schema, out _);
        var operation = CompileLowered(
            schema,
            """
            { product(id: "1") { name ... @defer(label: "price") { price } } }
            """);
        var planned = ImmutableArray.Create(
            new DeliveryGroup("other", null, 0) { Id = 0, Path = SelectionPath.Root.AppendField("product") });

        // act
        var valid = planner.TryValidateCompiledDeliveryGroups(operation, planned, out var cause);

        // assert
        Assert.False(valid);
        Assert.Equal("Delivery group 0 differs between the planned and the compiled operation.", cause);
    }

    [Fact]
    public void ValidateCompiledDeliveryGroups_Should_Fail_When_TheNumberOfGroupsDiffers()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);
        var planner = CreatePlanner(schema, out _);
        var operation = CompileLowered(
            schema,
            """
            { product(id: "1") { name ... @defer(label: "price") { price } } }
            """);

        // act
        var valid = planner.TryValidateCompiledDeliveryGroups(operation, [], out var cause);

        // assert
        Assert.False(valid);
        Assert.Equal("The compiled operation has 1 delivery groups, the planned one has 0.", cause);
    }

    [Fact]
    public void ValidateCompiledDeliveryGroups_Should_Succeed_When_TheGroupsAgree()
    {
        // arrange
        var schema = ComposeSchema(ProductSchema, PriceSchema);
        var planner = CreatePlanner(schema, out _);
        var operation = CompileLowered(
            schema,
            """
            { product(id: "1") { name ... @defer(label: "price") { price } } }
            """);
        var planned = ImmutableArray.Create(
            new DeliveryGroup("price", null, 0) { Id = 0, Path = SelectionPath.Root.AppendField("product") });

        // act
        var valid = planner.TryValidateCompiledDeliveryGroups(operation, planned, out var cause);

        // assert
        Assert.True(valid);
        Assert.Null(cause);
    }

    [Fact]
    public void Plan_Should_PlanThePartThroughTheLookupOfTheItemType_When_TheRootSelectionDeliversTheList()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                products { name }
                ... @defer(label: "top") { products { price } }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanOneGroupWithTheStepsOfItsPlaceAndOfItsPart_When_AFragmentHasOwnFieldsAndAPart()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema, ReviewKeySchema, ReviewRatingSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(label: "b") { price reviews { rating } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_ParentTheGroupOfThePart_When_TheFragmentIsNestedInADeferredFragment()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema, ReviewKeySchema, ReviewRatingSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(label: "outer") {
                        price
                        ... @defer(label: "inner") { reviews { rating } }
                    }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_NumberTheGroupsByThePlaceOfTheirFragments_When_AFragmentHasOnlyAPart()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema, ReviewKeySchema, ReviewRatingSchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(label: "x") { reviews { rating } }
                    ... @defer(label: "y") { price }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanTheSwitchOfThePart_When_TheFragmentIsSwitchable()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, ReviewKeySchema, ReviewRatingSchema);

        // act
        var result = Plan(
            schema,
            """
            query($d: Boolean!) {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(if: $d, label: "b") { reviews { rating } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_PlanWithoutDeliveryGroups_When_TheItemTypeOfTheListHasNoLookup()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, ReviewBodySchema);

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    reviews { id }
                    ... @defer(label: "d") { reviews { body } }
                }
            }
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        Assert.Empty(result.Plan.DeliveryGroups);
    }

    [Fact]
    public void Plan_Should_ParentTheNestedGroup_When_ItsFragmentStandsInAPart()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
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
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Plan_Should_KeepTheGroupOfThePartAtItsPlace_When_TheNormalizerRemovesANumberedCopy()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema);

        // act
        var result = Plan(
            schema,
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
            """);

        // assert
        Assert.Empty(result.Fallbacks);
        MatchSnapshot(result.Plan);
    }

    [Fact]
    public void Partition_Should_ReadTheGroupsFromTheOperationText_When_TheOperationHasAPart()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema, ReviewKeySchema, ReviewRatingSchema);
        var operation = Utf8GraphQLParser.Parse(
            """
            {
                product(id: "1") {
                    name
                    reviews {
                        id
                        ... @defer(label: "x", __group: 2, __parent: -1, __levels: 1) {
                            rating
                            ... @defer(label: "z", __group: 5, __parent: 2) { id }
                        }
                    }
                    ... @defer(label: "y", __group: 1, __parent: -1) { price }
                }
            }
            """).Definitions.OfType<OperationDefinitionNode>().Single();

        // act
        var partitioning = DeferPartitioner.Partition(
            operation,
            new DeferConditionCollection(CreateCompiler(schema).MaxAllowedDeferConditions));

        // assert
        partitioning.AllDeliveryGroups
            .Select(g => $"{g.Id} {g.Label} {g.Path} parent {g.Parent?.Id}")
            .MatchInlineSnapshots(
            [
                "0 y $.product parent ",
                "1 x $.product parent ",
                "2 z $.product.reviews parent 1"
            ]);
    }

    [Fact]
    public void Partition_Should_ReportANumberingError_When_OnlySomeFragmentsCarryAGroupNumber()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema);
        var operation = Utf8GraphQLParser.Parse(
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "x", __group: 0, __parent: -1) { price }
                    ... @defer(label: "y") { rating }
                }
            }
            """).Definitions.OfType<OperationDefinitionNode>().Single();

        // act
        var partitioning = DeferPartitioner.Partition(
            operation,
            new DeferConditionCollection(CreateCompiler(schema).MaxAllowedDeferConditions));

        // assert
        partitioning.NumberingError.MatchInlineSnapshot(
            "A @defer fragment carries no group number while another one does.");
        partitioning.AllDeliveryGroups
            .Select(g => $"{g.Id} {g.Label} {g.Path}")
            .MatchInlineSnapshots(["0 x $.product", "1 y $.product"]);
    }

    [Fact]
    public void ValidateCompiledDeliveryGroups_Should_Fail_When_TheGroupNumbersAreInconsistent()
    {
        // arrange
        var schema = ComposeSchema(ProductListSchema, PriceSchema);
        var planner = CreatePlanner(schema, out _);
        var operation = CreateCompiler(schema).Compile(
            "1",
            "1",
            "1",
            Normalize(
                schema,
                """
                {
                    product(id: "1") {
                        name
                        ... @defer(label: "x", __group: 0, __parent: -1) { price }
                        ... @defer(label: "y") { rating }
                    }
                }
                """),
            mergeAcrossDefer: false);

        // act
        var valid = planner.TryValidateCompiledDeliveryGroups(operation, [], out var cause);

        // assert
        Assert.False(valid);
        cause.MatchInlineSnapshot("A @defer fragment carries no group number while another one does.");
    }

    private const string ProductListSchema =
        """
        # name: a
        type Query {
            product(id: ID!): Product @lookup
            products: [Product!]!
        }

        type Product @key(fields: "id") {
            id: ID!
            name: String!
        }
        """;

    private const string ReviewKeySchema =
        """
        # name: c
        type Query {
            productByIdForReviews(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            reviews: [Review!]!
        }

        type Review @key(fields: "id") {
            id: ID! @shareable
        }
        """;

    private const string ReviewRatingSchema =
        """
        # name: d
        type Query {
            reviewById(id: ID!): Review @lookup
        }

        type Review @key(fields: "id") {
            id: ID!
            rating: Int!
        }
        """;

    private const string ReviewBodySchema =
        """
        # name: c
        type Query {
            productByIdForReviews(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            reviews: [Review!]!
        }

        type Review {
            id: ID!
            body: String!
        }
        """;

    private const string ProductSchema =
        """
        # name: a
        type Query {
            product(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            name: String!
        }
        """;

    private const string PriceSchema =
        """
        # name: b
        type Query {
            productById(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            price: Float!
            rating: Int!
        }
        """;

    private const string ReviewsSchema =
        """
        # name: c
        type Query {
            productByIdForReviews(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            reviews: [Review!]!
        }

        type Review {
            body: String!
        }
        """;

    private const string ProductBySkuSchema =
        """
        # name: b
        type Query {
            productBySku(sku: String!): Product @lookup
        }

        type Product @key(fields: "sku") {
            sku: String!
            price: Float!
        }
        """;

    private const string ProductKeysSchema =
        """
        # name: c
        type Query {
            productByIdForSku(id: ID!): Product @lookup
            productBySkuForId(sku: String!): Product @lookup
        }

        type Product @key(fields: "id") @key(fields: "sku") {
            id: ID!
            sku: String!
        }
        """;

    private const string CarrierSchema =
        """
        # name: b
        type Query {
            productById(id: ID!): Product @lookup
            carrierById(id: ID!): Carrier @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            carrier(weight: Int @require(field: "weight")): Carrier
        }

        type Carrier @key(fields: "id") {
            id: ID!
            name: String!
        }
        """;

    private const string RatingSchema =
        """
        # name: d
        type Query {
            carrierByIdD(id: ID!): Carrier @lookup
        }

        type Carrier @key(fields: "id") {
            id: ID!
            rating: Int!
        }
        """;

    private const string UserSchema =
        """
        # name: a
        type Query {
            user(id: Int!): User
            userById(id: Int!): User @lookup @internal
        }

        type Mutation {
            createUser(id: Int!): User
        }

        type User @key(fields: "id") {
            id: Int!
            email: String! @shareable
        }
        """;

    private const string UserSkuSchema =
        """
        # name: b
        type Query {
            userByIdB(id: Int!): User @lookup @internal
        }

        type User @key(fields: "id") {
            id: Int!
            sku: String
            soft: String
        }
        """;

    private const string UserTotalSchema =
        """
        # name: c
        type Query {
            userByEmail(email: String!): User @lookup @internal
        }

        type User @key(fields: "email") {
            email: String! @shareable
            total(sku: String @require(field: "sku")): String
        }
        """;

    private const string CatalogSchema =
        """
        # name: a
        type Query {
            products: [Product!]!
            product: Product
            productByUpc(upc: String!): Product @lookup
        }

        type Product @key(fields: "upc") {
            upc: String!
            name: String!
            eur(price: Int @require(field: "price(cur: \"EUR\")")): Int
            usd(price: Int @require(field: "price(cur: \"USD\")")): Int
            both(price: Int @require(field: "price(cur: \"EUR\", tax: true)")): Int
            netWeight(weight: Int @require(field: "weight")): Int
        }
        """;

    private const string PriceListSchema =
        """
        # name: b
        type Query {
            productByUpcB(upc: String!): Product @lookup
        }

        type Product @key(fields: "upc") {
            upc: String!
            price(cur: String, tax: Boolean): Int
            weight: Int
        }
        """;

    private const string ForeignRequirementSchema =
        """
        # name: c
        type Query {
            productByUpcC(upc: String!): Product @lookup
        }

        type Product @key(fields: "upc") {
            upc: String!
            eurC(price: Int @require(field: "price(cur: \"EUR\")")): Int
            weightC(weight: Int @require(field: "weight")): Int
            chained(eur: Int @require(field: "eur")): Int
        }
        """;

    private const string ShippingCostSchema =
        """
        # name: b
        type Query {
            productById(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            shippingCost(weight: Int @require(field: "weight")): Int!
        }
        """;

    private const string WeightSchema =
        """
        # name: c
        type Query {
            productByIdForWeight(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            weight: Int!
        }
        """;

    private const string PriceAuthorSchema =
        """
        # name: b
        type Query {
            productById(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            price: Float!
            author: Author! @shareable
        }

        type Author @key(fields: "id") {
            id: ID!
        }
        """;

    private const string StockAuthorSchema =
        """
        # name: d
        type Query {
            productByIdForStock(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            stock: Int!
            author: Author! @shareable
        }

        type Author @key(fields: "id") {
            id: ID!
        }
        """;

    private const string AuthorBioSchema =
        """
        # name: c
        type Query {
            authorById(id: ID!): Author @lookup
        }

        type Author @key(fields: "id") {
            id: ID!
            bio: String!
        }
        """;

    private const string AuthorAndEditorSchema =
        """
        # name: b
        type Query {
            productById(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            price: Float!
            author: Author!
            editor: Author!
        }

        type Author @key(fields: "id") {
            id: ID!
        }
        """;

    private const string AuthorBioAndNickSchema =
        """
        # name: c
        type Query {
            authorById(id: ID!): Author @lookup
        }

        type Author @key(fields: "id") {
            id: ID!
            bio: String!
            nick: String!
        }
        """;

    private const string ItemSchema =
        """
        # name: a
        type Query {
            item(id: ID!): Item
            book(id: ID!): Book @lookup
        }

        interface Item {
            id: ID!
            name: String!
        }

        type Book implements Item @key(fields: "id") {
            id: ID!
            name: String!
        }
        """;

    private const string PricedBookSchema =
        """
        # name: b
        type Query {
            bookById(id: ID!): Book @lookup
        }

        interface Priced {
            price: Float!
        }

        type Book implements Priced @key(fields: "id") {
            id: ID!
            price: Float!
            rating: Int!
        }
        """;

    private const string BookReviewsSchema =
        """
        # name: c
        type Query {
            bookByIdForReviews(id: ID!): Book @lookup
        }

        type Book @key(fields: "id") {
            id: ID!
            reviews: [Review!]!
        }

        type Review {
            body: String!
        }
        """;

    private const string NodeProductSchema =
        """
        # name: a
        type Query {
            node(id: ID!): Node @lookup @shareable
            product(id: ID!): Product @lookup
        }

        interface Node {
            id: ID!
        }

        type Product implements Node @key(fields: "id") {
            id: ID!
            name: String!
        }
        """;

    private const string NodePriceSchema =
        """
        # name: b
        type Query {
            node(id: ID!): Node @lookup @shareable
            productById(id: ID!): Product @lookup
        }

        interface Node {
            id: ID!
        }

        type Product implements Node @key(fields: "id") {
            id: ID!
            price: Float!
        }
        """;

    private static PlanResult Plan(FusionSchemaDefinition schema, string operationText)
        => Plan(
            schema,
            operationText,
            new OperationPlannerOptions { EnableSinglePlanDefer = true },
            "123456789101112");

    private static PlanResult Plan(
        FusionSchemaDefinition schema,
        string operationText,
        OperationPlannerOptions options,
        string id)
    {
        var planner = CreatePlanner(schema, options, out var fallbacks);
        var operation = Normalize(schema, operationText);
        var plan = planner.CreatePlan(id, id, id, operation);

        return new PlanResult(plan, [.. fallbacks]);
    }

    private static string FormatPlanWithoutDefer(FusionSchemaDefinition schema, string operationText)
        => FormatPlanWithoutDefer(
            schema,
            operationText,
            new OperationPlannerOptions { EnableSinglePlanDefer = true },
            "123456789101112");

    private static string FormatPlanWithoutDefer(
        FusionSchemaDefinition schema,
        string operationText,
        OperationPlannerOptions options,
        string id)
        => Format(Plan(schema, DeferDirective().Replace(operationText, string.Empty), options, id).Plan);

    private static string Format(OperationPlan plan)
        => new YamlOperationPlanFormatter().Format(plan);

    [GeneratedRegex(@"@defer(\([^)]*\))?")]
    private static partial Regex DeferDirective();

    private static OperationPlanner CreatePlanner(FusionSchemaDefinition schema, out List<string> fallbacks)
        => CreatePlanner(schema, new OperationPlannerOptions { EnableSinglePlanDefer = true }, out fallbacks);

    private static OperationPlanner CreatePlanner(
        FusionSchemaDefinition schema,
        OperationPlannerOptions options,
        out List<string> fallbacks)
    {
        var planner = new OperationPlanner(schema, CreateCompiler(schema), options, TestPolicyResolver.Create());
        var causes = new List<string>();
        planner.SinglePlanDeferFallback += (_, cause) => causes.Add(cause);
        fallbacks = causes;
        return planner;
    }

    private static DeferPartitioningResult Partition(FusionSchemaDefinition schema, string operationText)
    {
        var lowered = DeferLowering.Lower(Normalize(schema, operationText), schema);

        return DeferPartitioner.Partition(
            lowered,
            new DeferConditionCollection(CreateCompiler(schema).MaxAllowedDeferConditions));
    }

    private static OperationCompiler CreateCompiler(FusionSchemaDefinition schema)
    {
        var pool = new DefaultObjectPool<OrderedDictionary<string, List<FieldSelectionNode>>>(
            new DefaultPooledObjectPolicy<OrderedDictionary<string, List<FieldSelectionNode>>>());

        return new OperationCompiler(schema, pool);
    }

    private static OperationDefinitionNode Normalize(FusionSchemaDefinition schema, string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var rewriter = new DocumentRewriter(
            schema,
            mergeAcrossDefer: false,
            removeStaticallyExcludedSelections: true);
        var normalized = rewriter.RewriteDocument(document, operationName: null);

        return normalized.Definitions.OfType<OperationDefinitionNode>().First();
    }

    private static Operation CompileLowered(FusionSchemaDefinition schema, string operationText)
    {
        var lowered = DeferLowering.Lower(Normalize(schema, operationText), schema);

        return CreateCompiler(schema).Compile("1", "1", "1", lowered, mergeAcrossDefer: false);
    }

    private static ImmutableList<PlanStep> CreateSteps(
        params (int Id, int[] DeliveryGroups, int[] Dependents)[] steps)
    {
        var definition = new OperationDefinitionNode(
            null,
            null,
            null,
            OperationType.Query,
            [],
            [],
            new SelectionSetNode([new FieldNode("__typename")]));
        var type = ComposeSchema(ProductSchema).QueryType;

        return [.. steps.Select(step => (PlanStep)new OperationPlanStep
        {
            Id = step.Id,
            Definition = definition,
            Type = type,
            RootSelectionSetId = 1,
            SelectionSets = [1],
            SchemaName = "a",
            Target = SelectionPath.Root,
            Source = SelectionPath.Root,
            DeliveryGroups = [.. step.DeliveryGroups],
            Dependents = [.. step.Dependents]
        })];
    }

    private static ImmutableArray<DeliveryGroup> CreateGroups(params int[] parents)
    {
        var groups = new List<DeliveryGroup>();

        for (var i = 0; i < parents.Length; i++)
        {
            var parent = parents[i] < 0 ? null : groups[parents[i]];
            groups.Add(new DeliveryGroup(null, parent, i) { Id = i, Path = SelectionPath.Root });
        }

        return [.. groups];
    }

    private sealed class PlannerEventListener(string operationId) : EventListener
    {
        private readonly ConcurrentQueue<int> _eventIds = [];

        public IReadOnlyList<int> EventIds => [.. _eventIds];

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.Equals(PlannerEventSource.EventSourceName, StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name.Equals(PlannerEventSource.EventSourceName, StringComparison.Ordinal)
                && eventData.Payload is [string id, ..]
                && id.Equals(operationId, StringComparison.Ordinal))
            {
                _eventIds.Enqueue(eventData.EventId);
            }
        }
    }

    private sealed record PlanResult(OperationPlan Plan, ImmutableArray<string> Fallbacks);
}
