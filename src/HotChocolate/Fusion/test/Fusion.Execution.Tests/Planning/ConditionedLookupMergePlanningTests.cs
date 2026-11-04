using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Planning;

public class ConditionedLookupMergePlanningTests : FusionTestBase
{
    [Fact]
    public void CreatePlan_Should_FetchEntityOnce_When_SiblingFragmentsDifferInConditionsOverDirectKey()
    {
        // arrange
        var schema = CreateDirectKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!, $b: Boolean!) {
              user {
                ... @include(if: $a) { total rating }
                ... @include(if: $b) { total rating }
              }
            }
            """);

        // assert
        MatchInline(
            plan,
            """
            operation:
              - document: |
                  query($a: Boolean!, $b: Boolean!) {
                    user {
                      id @fusion__requirement
                      ... @include(if: $a) {
                        total
                        rating
                      }
                      ... @include(if: $b) {
                        total
                        rating
                      }
                    }
                  }
                hash: 123456789101112
                searchSpace: 1
                expandedNodes: 2
            nodes:
              - id: 1
                type: Operation
                schema: a
                operation: |
                  query Op_123456789101112_1($a: Boolean!, $b: Boolean!) {
                    user {
                      ... @include(if: $a) {
                        __typename
                      }
                      ... @include(if: $b) {
                        __typename
                      }
                      id
                    }
                  }
                forwardedVariables:
                  - a
                  - b
              - id: 2
                type: Operation
                schema: b
                operation: |
                  query Op_123456789101112_2($a: Boolean!, $b: Boolean!, $__fusion_1_id: ID!) {
                    userById(id: $__fusion_1_id) {
                      ... @include(if: $a) {
                        total
                        rating
                      }
                      ... @include(if: $b) {
                        total
                        rating
                      }
                    }
                  }
                source: $.userById
                target: $.user
                requirements:
                  - name: __fusion_1_id
                    selectionMap: >-
                      id
                forwardedVariables:
                  - a
                  - b
                dependencies:
                  - id: 1
            """);
    }

    [Fact]
    public void CreatePlan_Should_FetchEntityOnce_When_SiblingFragmentsDifferInConditionsOverOneKeyHop()
    {
        // arrange
        var schema = CreateOneHopKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!, $b: Boolean!) {
              user {
                ... @include(if: $a) { total rating }
                ... @include(if: $b) { total rating }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void CreatePlan_Should_FetchEntityOnce_When_SiblingFragmentsDifferInConditionsOverTwoKeyHops()
    {
        // arrange
        var schema = CreateTwoHopKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!, $b: Boolean!) {
              user {
                ... @include(if: $a) { total rating }
                ... @include(if: $b) { total rating }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void CreatePlan_Should_FetchEntityOnce_When_NestedFragmentsDifferInConditions()
    {
        // arrange
        var schema = CreateOneHopKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!, $b: Boolean!) {
              user {
                ... {
                  id
                  ... @skip(if: $b) { name total }
                }
                ... @skip(if: $a) {
                  id
                  ... { name total }
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void CreatePlan_Should_KeepSharedConditionOnTheLookup_When_FragmentsShareAnOuterCondition()
    {
        // arrange
        var schema = CreateOneHopKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!, $b: Boolean!, $c: Boolean!) {
              user {
                ... @include(if: $a) {
                  ... @skip(if: $b) { total rating }
                  ... @skip(if: $c) { total rating }
                }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void CreatePlan_Should_FetchEntityOnce_When_UnconditionalFieldsAccompanyAConditionalFragment()
    {
        // arrange
        var schema = CreateOneHopKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!) {
              user {
                total
                ... @include(if: $a) { rating bonus }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void CreatePlan_Should_KeepNodeCondition_When_OnlyOneFragmentNeedsTheLookup()
    {
        // arrange
        var schema = CreateOneHopKeySchema();

        // act
        var plan = PlanOperation(
            schema,
            """
            query ($a: Boolean!) {
              user {
                ... @include(if: $a) { total rating }
              }
            }
            """);

        // assert
        MatchSnapshot(plan);
    }

    private static FusionSchemaDefinition CreateDirectKeySchema()
        => ComposeSchema(
            """
            # name: a
            schema { query: Query }

            type Query {
              user: User
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: b
            schema { query: Query }

            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              total: Int!
              rating: Int!
              bonus: Int!
            }
            """,
            """
            # name: e
            schema { query: Query }

            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              name: String!
            }
            """);

    private static FusionSchemaDefinition CreateOneHopKeySchema()
        => ComposeSchema(
            """
            # name: a
            schema { query: Query }

            type Query {
              user: User
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: b
            schema { query: Query }

            type Query {
              userBySku(sku: String!): User @lookup @internal
            }

            type User @key(fields: "sku") {
              sku: String! @shareable
              total: Int!
              rating: Int!
              bonus: Int!
            }
            """,
            """
            # name: c
            schema { query: Query }

            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              sku: String! @shareable
            }
            """,
            """
            # name: e
            schema { query: Query }

            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              name: String!
            }
            """);

    private static FusionSchemaDefinition CreateTwoHopKeySchema()
        => ComposeSchema(
            """
            # name: a
            schema { query: Query }

            type Query {
              user: User
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
            }
            """,
            """
            # name: b
            schema { query: Query }

            type Query {
              userBySku(sku: String!): User @lookup @internal
            }

            type User @key(fields: "sku") {
              sku: String! @shareable
              total: Int!
              rating: Int!
            }
            """,
            """
            # name: c
            schema { query: Query }

            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
              id: ID!
              code: String! @shareable
            }
            """,
            """
            # name: d
            schema { query: Query }

            type Query {
              userByCode(code: String!): User @lookup @internal
            }

            type User @key(fields: "code") {
              code: String! @shareable
              sku: String! @shareable
            }
            """);
}
