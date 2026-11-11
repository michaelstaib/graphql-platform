using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Nodes.Serialization;

namespace HotChocolate.Fusion.Planning;

public class ValueTypeResultSelectionSetPlanningTests : FusionTestBase
{
    [Fact]
    public void Plan_Should_KeepTheSubFieldsInTheResultSelectionSet_When_TheValueTypeIsAUnion()
    {
        // arrange
        var schema = ComposeSchema(
            UsersSchema,
            """
            # name: b
            type Query {
              lookups: Lookups! @internal
            }

            type Lookups @internal {
              userById(id: Int!): User @lookup
            }

            type User @key(fields: "id") {
              id: Int!
              info: Info @shareable
            }

            union Info = InfoA | InfoB

            type InfoA @shareable {
              code: String
            }

            type InfoB @shareable {
              code2: String
            }
            """,
            """
            # name: x
            type Query {
              lookups: Lookups! @internal
            }

            type Lookups @internal {
              userById(id: Int!): User @lookup
            }

            type User @key(fields: "id") {
              id: Int!
              info: Info @shareable
            }

            union Info = InfoA | InfoB

            type InfoA @shareable {
              label: String
            }

            type InfoB @shareable {
              label2: String
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            {
              users {
                info {
                  ... on InfoA {
                    code
                    label
                  }
                }
              }
            }
            """);

        // assert
        FormatIndented(plan).MatchSnapshot(extension: ".json");
    }

    [Fact]
    public void Plan_Should_KeepTheSubFieldsInTheResultSelectionSet_When_TheValueTypeIsAnInterface()
    {
        // arrange
        var schema = ComposeSchema(
            UsersSchema,
            """
            # name: b
            type Query {
              lookups: Lookups! @internal
            }

            type Lookups @internal {
              userById(id: Int!): User @lookup
            }

            type User @key(fields: "id") {
              id: Int!
              info: Info @shareable
            }

            interface Info {
              sku: String
            }

            type InfoA implements Info @shareable {
              sku: String
              code: String
            }
            """,
            """
            # name: x
            type Query {
              lookups: Lookups! @internal
            }

            type Lookups @internal {
              userById(id: Int!): User @lookup
            }

            type User @key(fields: "id") {
              id: Int!
              info: Info @shareable
            }

            interface Info {
              sku: String
            }

            type InfoA implements Info @shareable {
              sku: String
              label: String
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            {
              users {
                info {
                  ... on InfoA {
                    code
                    label
                  }
                }
              }
            }
            """);

        // assert
        FormatIndented(plan).MatchSnapshot(extension: ".json");
    }

    [Fact]
    public void Plan_Should_StripTheSubFieldsFromTheResultSelectionSet_When_TheUnionMembersAreEntities()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: a
            type Query {
              item: Item @shareable
            }

            union Item = Product | Book

            type Product @key(fields: "id") {
              id: Int!
              name: String
            }

            type Book @key(fields: "id") {
              id: Int!
              title: String
            }
            """,
            """
            # name: b
            type Query {
              item: Item @shareable
              lookups: Lookups! @internal
            }

            type Lookups @internal {
              productById(id: Int!): Product @lookup
              bookById(id: Int!): Book @lookup
            }

            union Item = Product | Book

            type Product @key(fields: "id") {
              id: Int!
              price: Int
            }

            type Book @key(fields: "id") {
              id: Int!
              pages: Int
            }
            """);

        // act
        var plan = PlanOperation(
            schema,
            """
            {
              item {
                ... on Product {
                  name
                  price
                }
                ... on Book {
                  title
                  pages
                }
              }
            }
            """);

        // assert
        FormatIndented(plan).MatchSnapshot(extension: ".json");
    }

    private const string UsersSchema =
        """
        # name: a
        type Query {
          users: [User]
        }

        type User @key(fields: "id") {
          id: Int!
        }
        """;

    private static string FormatIndented(OperationPlan plan)
        => JsonNode.Parse(new JsonOperationPlanFormatter().Format(plan))!
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
