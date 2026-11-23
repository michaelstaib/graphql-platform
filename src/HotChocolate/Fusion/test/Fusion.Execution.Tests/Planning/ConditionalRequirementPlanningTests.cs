using System.Text;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Planning;

public class ConditionalRequirementPlanningTests : FusionTestBase
{
    [Fact]
    public void Plan_Should_ShareLookup_When_OnlyOneFieldWithNullableRequirementIsSkipped()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                shipping
                tax @skip(if: $a)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_SkippedFieldWithNullableRequirementComesFirst()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                tax @skip(if: $a)
                shipping
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_SiblingFieldWithRequirementIsNotIncluded()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                shipping
                tax @include(if: $a)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_FetchRequirementInOwnLookup_When_FieldsWithRequirementsAreSkippedByDifferentVariables()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user {
                shipping @skip(if: $b)
                tax @skip(if: $a)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_KeepOwnLookup_When_RequirementArgumentOfSkippedFieldIsNonNull()
    {
        // arrange
        var schema = ComposeNonNullUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                shipping
                tax @skip(if: $a)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_KeepOwnLookups_When_FieldsAreSkippedByUnrelatedVariables()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user {
                shipping @include(if: $b)
                tax @skip(if: $a)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_SelectRequirementUnderFieldCondition_When_FieldIsMergedIntoUnconditionalLookup()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                shipping
                ... @include(if: $a) {
                  tax
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_ConditionsFormAChain()
    {
        // arrange
        var schema = ComposeChainSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user {
                shipping
                tax @skip(if: $a)
                fee @skip(if: $a) @include(if: $b)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_SelectRequirementOnce_When_ConditionalAndUnconditionalFieldRequireTheSameField()
    {
        // arrange
        var schema = ComposeChainSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                tax @skip(if: $a)
                ship2
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ForwardVariableOfRequirementCondition_When_RequirementHasConditions()
    {
        // arrange
        var schema = ComposeChainSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user {
                shipping
                tax @skip(if: $a)
                ... @include(if: $b) {
                  fee
                }
              }
            }
            """);

        // assert
        DescribeRequirementConditions(plan).MatchInlineSnapshot(
            """
            __fusion_2_weight: [] forwards [a, b]
            __fusion_5_size: [$b=True] forwards [a, b]
            __fusion_6_total: [$a=False] forwards [a, b]
            """);
    }

    [Fact]
    public void Plan_Should_KeepOwnLookup_When_FieldIsServedByApolloFederationSource()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: products
            extend schema @link(url: "https://specs.apollo.dev/federation/v2.7", import: ["@key"])

            type Query {
              topProducts: [Product!]!
            }

            type Product @key(fields: "upc") {
              upc: String!
              price: Int!
              weight: Int!
            }
            """,
            """
            # name: inventory
            extend schema @link(
              url: "https://specs.apollo.dev/federation/v2.7"
              import: ["@external", "@key", "@requires"]
            )

            type Product @key(fields: "upc") {
              upc: String! @external
              price: Int! @external
              weight: Int! @external
              shippingEstimate: Int @requires(fields: "weight")
              taxEstimate: Int @requires(fields: "price")
            }
            """,
            """
            # name: reviews
            extend schema @link(url: "https://specs.apollo.dev/federation/v2.7", import: ["@external", "@key"])

            type Product @key(fields: "upc") {
              upc: String! @external
              reviews: [Review!]!
            }

            type Review @key(fields: "id") {
              id: ID!
              product: Product
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              topProducts {
                reviews {
                  product {
                    shippingEstimate
                    taxEstimate @skip(if: $a)
                  }
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_FieldsWithRequirementsCarryTheSameCondition()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                shipping @skip(if: $a)
                tax @skip(if: $a)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_FieldsWithRequirementsCarryNoCondition()
    {
        // arrange
        var schema = ComposeUserSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            {
              user {
                shipping
                tax
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_SiblingFieldWithRequirementIsSkippedBelowTypeCondition()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              item: Item!
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            type Product @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            type Product @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              item {
                ... on Product {
                  shipping
                  tax @skip(if: $a)
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ResolveRequirements_When_RootFieldsServeOneConditionalFieldWithRequirement()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              viewer: Viewer @shareable
            }

            type Viewer {
              name: String
            }
            """,
            """
            # name: d
            type Query {
              viewer: Viewer @shareable
            }

            type Viewer {
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              viewer: Viewer @shareable
            }

            type Viewer {
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              viewer {
                tax @skip(if: $a)
                shipping
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_InlineRequirementIntoParentLookup_When_ConditionSitsOnAncestorField()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
              orderById(id: ID!): Order @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              order: Order!
            }

            type Order @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              orderById(id: ID!): Order @lookup @internal
            }

            type Order @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user @skip(if: $a) {
                order {
                  shipping
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_InlineRequirementIntoParentLookup_When_ConditionSitsAboveTypeCondition()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
              productById(id: ID!): Product @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              item: Item!
            }

            interface Item @key(fields: "id") {
              id: ID!
              dItemField: String
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              dItemField: String
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: c
            type Query {
              itemById(id: ID!): Item @lookup @internal
            }

            interface Item @key(fields: "id") {
              id: ID!
              sku: ID!
            }

            type Product implements Item @key(fields: "id") @key(fields: "sku") {
              id: ID!
              sku: ID!
            }
            """,
            """
            # name: b
            type Query {
              productBySku(sku: ID!): Product @lookup @internal
            }

            type Product @key(fields: "sku") {
              sku: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user @skip(if: $a) {
                item {
                  dItemField
                  ... on Product {
                    shipping
                  }
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_InlineRequirementIntoParentLookup_When_InterfaceSelectionIsPlannedPerConcreteType()
    {
        // arrange
        var schema = ComposeItemSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user @skip(if: $a) {
                item {
                  shipping
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_ShareLookup_When_SiblingFieldIsSkippedBelowSharedInterfaceField()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              item: Item! @shareable
            }

            interface Item {
              id: ID!
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
              productById(id: ID!): Product @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              item: Item! @shareable
            }

            interface Item {
              id: ID!
              weight: Int!
              total: Int!
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user @skip(if: $a) {
                item {
                  shipping
                  tax @skip(if: $b)
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_InlineRequirementIntoBranchLookup_When_ConditionSitsOnInterfaceTypeConditionBelowNode()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              node(id: ID!): Node @lookup
            }

            interface Node {
              id: ID!
            }

            type Product implements Node @key(fields: "id") {
              id: ID!
              name: String
            }
            """,
            """
            # name: d
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
              weight: Int!
              total: Int!
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              node(id: "UHJvZHVjdDox") {
                ... on Item @skip(if: $a) {
                  weight
                  shipping
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_FetchRequirementThroughParentLookup_When_SourceOfRequirementHasNoLookupForTheEntity()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              order: Order! @shareable
            }

            type Order @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              order: Order! @shareable
            }

            type Order @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              orderById(id: ID!): Order @lookup @internal
            }

            type Order @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                order {
                  tax @skip(if: $a)
                  shipping
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_KeepUnconditionalFieldOutOfConditionalLookup_When_RequirementIsSharedWithOtherSource()
    {
        // arrange
        var schema = ComposeTwoRequiringSourcesSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            "User");

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user {
                tax @skip(if: $a)
                label
                shipping @skip(if: $a) @include(if: $b)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_KeepUnconditionalFieldOutOfConditionalLookup_When_SharedRequirementSitsBelowChildEntity()
    {
        // arrange
        var schema = ComposeTwoRequiringSourcesSchema(
            """
            # name: a
            type Query {
              user: User!
            }

            type User {
              id: ID!
              order: Order!
            }

            type Order @key(fields: "id") {
              id: ID!
            }
            """,
            "Order");

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              user {
                order {
                  label
                  tax @skip(if: $a)
                  shipping @skip(if: $a) @include(if: $b)
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    private static FusionSchemaDefinition ComposeTwoRequiringSourcesSchema(string sourceA, string entityType)
    {
        var lookup = char.ToLowerInvariant(entityType[0]) + entityType[1..] + "ById";

        return ComposeSchema(
            sourceA,
            $$"""
            # name: d
            type Query {
              {{lookup}}(id: ID!): {{entityType}} @lookup @internal
            }

            type {{entityType}} @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            $$"""
            # name: b
            type Query {
              {{lookup}}(id: ID!): {{entityType}} @lookup @internal
            }

            type {{entityType}} @key(fields: "id") {
              id: ID!
              label: String
              shipping(weight: Int @require(field: "weight")): Int
            }
            """,
            $$"""
            # name: c
            type Query {
              {{lookup}}(id: ID!): {{entityType}} @lookup @internal
            }

            type {{entityType}} @key(fields: "id") {
              id: ID!
              rating: Int
              tax(total: Int @require(field: "total")): Int
            }
            """);
    }

    private static FusionSchemaDefinition ComposeItemSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
              productById(id: ID!): Product @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              item: Item!
            }

            interface Item {
              id: ID!
              weight: Int!
              total: Int!
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            interface Item {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }

            type Product implements Item @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);

    private static string DescribeRequirementConditions(OperationPlan plan)
    {
        var description = new StringBuilder();

        foreach (var node in plan.AllNodes)
        {
            switch (node)
            {
                case OperationExecutionNode operation:
                    Describe(description, operation.Requirements, operation.ForwardedVariables);
                    break;

                case OperationBatchExecutionNode batch:
                    foreach (var definition in batch.Operations)
                    {
                        Describe(description, definition.Requirements, definition.ForwardedVariables);
                    }

                    break;
            }
        }

        return description.ToString().TrimEnd();

        static void Describe(
            StringBuilder description,
            ReadOnlySpan<OperationRequirement> requirements,
            ReadOnlySpan<string> forwardedVariables)
        {
            var forwarded = string.Join(", ", forwardedVariables.ToArray().Order(StringComparer.Ordinal));

            foreach (var requirement in requirements)
            {
                if (requirement.Key.EndsWith("_id", StringComparison.Ordinal))
                {
                    continue;
                }

                var conditions = requirement.Conditions is null
                    ? string.Empty
                    : string.Join(", ", requirement.Conditions.Select(c => $"${c.VariableName}={c.PassingValue}"));
                description.AppendLine($"{requirement.Key}: [{conditions}] forwards [{forwarded}]");
            }
        }
    }

    private static FusionSchemaDefinition ComposeChainSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
              size: Int!
            }
            """,
            """
            # name: b
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
              fee(size: Int @require(field: "size")): Int
              ship2(total: Int @require(field: "total")): Int
            }
            """);

    private static FusionSchemaDefinition ComposeNonNullUserSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              users: [User!]!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int! @require(field: "total")): Int
            }
            """);

    private static FusionSchemaDefinition ComposeUserSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              user: User!
              users: [User!]!
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: d
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              shipping(weight: Int @require(field: "weight")): Int
              tax(total: Int @require(field: "total")): Int
            }
            """);
}
