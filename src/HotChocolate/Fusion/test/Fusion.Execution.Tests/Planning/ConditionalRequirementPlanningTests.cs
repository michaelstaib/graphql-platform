using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Planning;

public class ConditionalRequirementPlanningTests : FusionTestBase
{
    [Fact]
    public void Plan_Should_FetchRequirementInOwnLookup_When_SiblingFieldWithRequirementIsSkipped()
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
    public void Plan_Should_FetchRequirementInOwnLookup_When_SkippedSiblingFieldWithRequirementComesFirst()
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
    public void Plan_Should_FetchRequirementInOwnLookup_When_SiblingFieldWithRequirementIsNotIncluded()
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
    public void Plan_Should_FetchRequirementInOwnLookup_When_SiblingFieldWithRequirementIsSkippedBelowTypeCondition()
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
