using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Nodes.Serialization;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Planning;

public class SinglePlanDeferPlannerTests : FusionTestBase
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

        // act
        var result = Plan(
            schema,
            """
            {
                product(id: "1") {
                    name
                    ... @defer(label: "details") { details { size weight } }
                }
            }
            """);

        // assert
        Assert.Equal(["No possible plan was found."], result.Fallbacks);
        Assert.Empty(result.Plan.DeliveryGroups);
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

    private static PlanResult Plan(FusionSchemaDefinition schema, string operationText)
    {
        var planner = CreatePlanner(schema, out var fallbacks);
        var operation = Normalize(schema, operationText);

        const string id = "123456789101112";
        var plan = planner.CreatePlan(id, id, id, operation);

        return new PlanResult(plan, [.. fallbacks]);
    }

    private static OperationPlanner CreatePlanner(FusionSchemaDefinition schema, out List<string> fallbacks)
    {
        var options = new OperationPlannerOptions { EnableSinglePlanDefer = true };
        var planner = new OperationPlanner(schema, CreateCompiler(schema), options, TestPolicyResolver.Create());
        var causes = new List<string>();
        planner.SinglePlanDeferFallback += (_, cause) => causes.Add(cause);
        fallbacks = causes;
        return planner;
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

    private sealed record PlanResult(OperationPlan Plan, ImmutableArray<string> Fallbacks);
}
