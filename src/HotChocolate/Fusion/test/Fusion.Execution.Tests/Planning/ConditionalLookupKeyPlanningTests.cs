using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Planning;

public class ConditionalLookupKeyPlanningTests : FusionTestBase
{
    [Fact]
    public void Plan_Should_FetchLookupKeyInOwnStep_When_StepWithKeyIsPlannedForSkippedSiblingField()
    {
        // arrange
        var schema = ComposeProductSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              products {
                upc
                eur @skip(if: $a)
                rating
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_KeepLookupKeyInConditionalStep_When_EverySelectionOfLookupCarriesTheCondition()
    {
        // arrange
        var schema = ComposeProductSchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!, $b: Boolean!) {
              products {
                upc
                eur @skip(if: $a)
                rating @skip(if: $a) @include(if: $b)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_FetchKeyOfRequirementLookupInOwnStep_When_StepWithKeyIsPlannedForSkippedSiblingField()
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
            }

            type User @key(fields: "id") @key(fields: "sku") {
              id: ID!
              sku: ID!
              weight: Int!
              total: Int!
            }
            """,
            """
            # name: b
            type Query {
              userBySku(sku: ID!): User @lookup @internal
            }

            type User @key(fields: "sku") {
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
    public void Plan_Should_KeepLookupsOfSiblingFieldsIndependent_When_FieldsOfOneSourceRunUnderOppositeConditions()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              product: Product!
              productByUpc(upc: String!): Product @lookup @internal
            }

            type Product @key(fields: "upc") {
              upc: String!
              eur(price: Int @require(field: "price(cur: \"EUR\")")): Int
              usd(price: Int @require(field: "price(cur: \"USD\")")): Int
            }
            """,
            """
            # name: b
            type Query {
              productByUpc(upc: String!): Product @lookup @internal
            }

            type Product @key(fields: "upc") {
              upc: String!
              price(cur: String): Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($v: Boolean!) {
              product {
                eur @include(if: $v)
                usd @skip(if: $v)
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Plan_Should_KeepLookupKeyInParentStep_When_ConditionSitsOnParentField()
    {
        // arrange
        var schema = ComposeSchema(
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
            """
            # name: d
            type Query {
              orderById(id: ID!): Order @lookup @internal
            }

            type Order @key(fields: "id") {
              id: ID!
              item: Item!
            }

            type Item @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: e
            type Query {
              itemById(id: ID!): Item @lookup @internal
            }

            type Item @key(fields: "id") {
              id: ID!
              price: Int!
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            query($a: Boolean!) {
              user {
                order @skip(if: $a) {
                  item {
                    price
                  }
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    private static FusionSchemaDefinition ComposeProductSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              products: [Product!]!
              productByUpc(upc: String!): Product @lookup
            }

            type Product @key(fields: "upc") {
              upc: String!
              eur(price: Int @require(field: "price(cur: \"EUR\")")): Int
            }
            """,
            """
            # name: b
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            type Product @key(fields: "id") {
              id: ID! @shareable
              price(cur: String): Int
            }
            """,
            """
            # name: s
            type Query {
              productByUpc(upc: String!): Product @lookup @internal
            }

            type Product @key(fields: "upc") {
              upc: String!
              id: ID! @shareable
            }
            """,
            """
            # name: t
            type Query {
              productById(id: ID!): Product @lookup @internal
            }

            type Product @key(fields: "id") {
              id: ID! @shareable
              rating: Int!
            }
            """);
}
