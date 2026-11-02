using HotChocolate.Buffers;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Nodes.Serialization;
using HotChocolate.Fusion.Types;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Execution;

public class OperationPlanKeyFetchesTests : FusionTestBase
{
    [Theory]
    [InlineData("Merged", "0: { sku }", "1: { sku }")]
    [InlineData("MergedWithSelection", "0: { sku }")]
    [InlineData("Batched", "0: { code } { sku }")]
    [InlineData("Nested", "0: ", "1: { sku } { sku } { sku }")]
    public void GetKeyFetches_Should_ReturnFetchesSupplyingTheKey_When_PlanIsCreated(
        string shape,
        params string[] expected)
    {
        // arrange
        var plan = CreatePlan(shape);

        // act
        var keyFetches = Describe(plan);

        // assert
        keyFetches.MatchInlineSnapshots(expected);
    }

    [Theory]
    [InlineData("Merged", "0: { sku }", "1: { sku }")]
    [InlineData("MergedWithSelection", "0: { sku }")]
    [InlineData("Batched", "0: { code } { sku }")]
    [InlineData("Nested", "0: ", "1: { sku } { sku } { sku }")]
    public void GetKeyFetches_Should_ReturnFetchesSupplyingTheKey_When_PlanIsDeserialized(
        string shape,
        params string[] expected)
    {
        // arrange
        var plan = CreatePlan(shape);
        var formatter = new JsonOperationPlanFormatter();
        using var buffer = new PooledArrayWriter();
        formatter.Format(buffer, plan);
        var compiler = new OperationCompiler(
            (FusionSchemaDefinition)plan.Operation.Schema,
            new DefaultObjectPool<OrderedDictionary<string, List<FieldSelectionNode>>>(
                new DefaultPooledObjectPolicy<OrderedDictionary<string, List<FieldSelectionNode>>>()));
        var parser = new JsonOperationPlanParser(compiler);

        // act
        var parsed = parser.Parse(buffer.WrittenMemory);

        // assert
        Describe(parsed).MatchInlineSnapshots(expected);
        formatter.Format(parsed).MatchInlineSnapshot(formatter.Format(plan));
    }

    private static string[] Describe(OperationPlan plan)
        => plan.IncrementalPlans
            .Select(incremental =>
            {
                var group = string.Join(",", incremental.DeliveryGroups.Select(t => t.Id));
                var fetches = plan.GetKeyFetches(incremental).ToArray().Select(t => t.ToString()).Order();
                return $"{group}: {string.Join(" ", fetches)}";
            })
            .ToArray();

    private static OperationPlan CreatePlan(string shape)
        => shape switch
        {
            "Merged" => PlanOperation(
                CreateSingleHopSchema(),
                """
                {
                  a: users(ids: [1]) { name ... @defer { total } }
                  b: users(ids: [2]) { name ... @defer { total } }
                }
                """),
            "MergedWithSelection" => PlanOperation(
                CreateSingleHopSchema(),
                """
                {
                  users(ids: [1]) { name ... @defer { total } }
                  more: users(ids: [2]) { sku }
                }
                """),
            "Batched" => PlanOperation(
                CreateTwoHopSchema(),
                """
                {
                  users(ids: [1]) { name ... @defer { total } }
                  more: users(ids: [2]) { other }
                }
                """),
            "Nested" => PlanOperation(
                CreateSingleHopSchema(),
                """
                query($s: Boolean!, $i: Boolean!) {
                  users(ids: [1]) {
                    name
                    ... @defer(if: $s, label: "a") {
                      sku
                      ... @defer(if: $i, label: "b") { total }
                    }
                  }
                }
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

    private static FusionSchemaDefinition CreateSingleHopSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              users(ids: [Int!]!): [User!]!
              userById(id: ID!): User @lookup @internal
            }

            type User {
              id: ID!
              name: String
            }
            """,
            """
            # name: b
            type Query {
              userBySku(sku: String!): User @lookup @internal
            }

            type User {
              sku: String @shareable
              total: Int
            }
            """,
            """
            # name: c
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User {
              id: ID!
              sku: String @shareable
            }
            """);

    private static FusionSchemaDefinition CreateTwoHopSchema()
        => ComposeSchema(
            """
            # name: a
            type Query {
              users(ids: [Int!]!): [User!]!
              userById(id: ID!): User @lookup @internal
            }

            type User {
              id: ID!
              name: String
            }
            """,
            """
            # name: b
            type Query {
              userBySku(sku: String!): User @lookup @internal
            }

            type User {
              sku: String @shareable
              total: Int
            }
            """,
            """
            # name: c
            type Query {
              userById(id: ID!): User @lookup @internal
            }

            type User {
              id: ID!
              code: String @shareable
              other: String
            }
            """,
            """
            # name: d
            type Query {
              userByCode(code: String!): User @lookup @internal
            }

            type User {
              code: String! @shareable
              sku: String @shareable
            }
            """);
}
