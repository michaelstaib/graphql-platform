using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Planning;

public class DeferMutationPlannerTests : FusionTestBase
{
    [Theory]
    [InlineData(Topology.DirectKey, MutationShape.Nested, "Mutation/a | Query/e | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.NestedTotalFirst, "Mutation/a | Query/b | Query/e")]
    [InlineData(Topology.DirectKey, MutationShape.Sibling, "Mutation/a | Query/e | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.NamedFragments, "Mutation/a | Query/e | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.IfVariable, "Mutation/a,Query/e,Query/b | Query/e | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.InnerIfVariable, "Mutation/a | Query/e | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.TwoRootFields, "Mutation/a,Mutation/a | Query/e | Query/b | Query/e")]
    [InlineData(Topology.DirectKey, MutationShape.ListField, "Mutation/a | Query/e | Query/b")]
    [InlineData(Topology.OneHopKey, MutationShape.Nested, "Mutation/a | Query/e,Query/c | Query/b")]
    [InlineData(Topology.OneHopKey, MutationShape.Sibling, "Mutation/a,Query/c | Query/e | Query/b")]
    [InlineData(Topology.OneHopKey, MutationShape.NamedFragments, "Mutation/a | Query/e,Query/c | Query/b")]
    [InlineData(
        Topology.OneHopKey,
        MutationShape.IfVariable,
        "Mutation/a,Query/e,Query/b,Query/c | Query/e,Query/c | Query/b")]
    [InlineData(Topology.OneHopKey, MutationShape.InnerIfVariable, "Mutation/a,Query/c | Query/e,Query/c | Query/b")]
    [InlineData(
        Topology.OneHopKey,
        MutationShape.TwoRootFields,
        "Mutation/a,Mutation/a | Query/e,Query/c | Query/b | Query/e")]
    [InlineData(Topology.OneHopKey, MutationShape.ListField, "Mutation/a | Query/e,Query/c | Query/b")]
    [InlineData(Topology.TwoHopKey, MutationShape.Nested, "Mutation/a | Query/e,Query/d,Query/c | Query/b")]
    [InlineData(Topology.TwoHopKey, MutationShape.Sibling, "Mutation/a,Query/d,Query/c | Query/e | Query/b")]
    [InlineData(Topology.TwoHopKey, MutationShape.NamedFragments, "Mutation/a | Query/e,Query/d,Query/c | Query/b")]
    [InlineData(
        Topology.TwoHopKey,
        MutationShape.IfVariable,
        "Mutation/a,Query/e,Query/b,Query/d,Query/c | Query/e,Query/d,Query/c | Query/b")]
    [InlineData(
        Topology.TwoHopKey,
        MutationShape.InnerIfVariable,
        "Mutation/a,Query/d,Query/c | Query/e,Query/d,Query/c | Query/b")]
    [InlineData(
        Topology.TwoHopKey,
        MutationShape.TwoRootFields,
        "Mutation/a,Mutation/a | Query/e,Query/d,Query/c | Query/b | Query/e")]
    [InlineData(Topology.TwoHopKey, MutationShape.ListField, "Mutation/a | Query/e,Query/d,Query/c | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.SingleIfVariable, "Mutation/a,Query/b | Query/b")]
    [InlineData(Topology.DirectKey, MutationShape.OuterIfVariable, "Mutation/a,Query/e | Query/e | Query/b")]
    [InlineData(
        Topology.DirectKey,
        MutationShape.OuterAndInnerIfVariables,
        "Mutation/a,Query/e,Query/b | Query/e | Query/b")]
    [InlineData(Topology.OneHopKey, MutationShape.SingleIfVariable, "Mutation/a,Query/b,Query/c | Query/b")]
    [InlineData(
        Topology.OneHopKey,
        MutationShape.OuterIfVariable,
        "Mutation/a,Query/e,Query/c | Query/e,Query/c | Query/b")]
    [InlineData(
        Topology.OneHopKey,
        MutationShape.OuterAndInnerIfVariables,
        "Mutation/a,Query/e,Query/b,Query/c | Query/e,Query/c | Query/b")]
    [InlineData(Topology.TwoHopKey, MutationShape.SingleIfVariable, "Mutation/a,Query/b,Query/d,Query/c | Query/b")]
    [InlineData(
        Topology.TwoHopKey,
        MutationShape.OuterIfVariable,
        "Mutation/a,Query/e,Query/d,Query/c | Query/e,Query/d,Query/c | Query/b")]
    [InlineData(
        Topology.TwoHopKey,
        MutationShape.OuterAndInnerIfVariables,
        "Mutation/a,Query/e,Query/b,Query/d,Query/c | Query/e,Query/d,Query/c | Query/b")]
    public void Defer_Should_KeepMutationRootFieldOutOfIncrementalPlans_When_ShapeIsPlanned(
        Topology topology,
        MutationShape shape,
        string expectedNodes)
    {
        // arrange
        var schema = ComposeTopology(topology);

        // act
        var plan = PlanOperation(schema, GetOperation(shape));

        // assert
        // the first entry is the main plan, every following entry is one incremental plan
        Assert.Equal(expectedNodes, DescribeNodes(plan));
    }

    [Fact]
    public void Defer_Should_ProduceOneMutationNodeInMainPlan_When_NestedDeferNeedsKeyOfOuterGroup()
    {
        // arrange
        var schema = ComposeTopology(Topology.DirectKey);

        // act
        var plan = PlanOperation(schema, GetOperation(MutationShape.Nested));

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Defer_Should_ProduceOneMutationNodeInMainPlan_When_NestedDeferNeedsTwoHopKey()
    {
        // arrange
        var schema = ComposeTopology(Topology.TwoHopKey);

        // act
        var plan = PlanOperation(schema, GetOperation(MutationShape.Nested));

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Defer_Should_RunKeyStepWithoutCondition_When_DeferConditionSkipsConsumer()
    {
        // arrange
        var schema = ComposeTopology(Topology.OneHopKey);

        // act
        var plan = PlanOperation(schema, GetOperation(MutationShape.SingleIfVariable));

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Defer_Should_RunKeyStepsWithoutCondition_When_TwoHopKeyIsSkippedByDeferCondition()
    {
        // arrange
        var schema = ComposeTopology(Topology.TwoHopKey);

        // act
        var plan = PlanOperation(schema, GetOperation(MutationShape.SingleIfVariable));

        // assert
        MatchSnapshot(plan);
    }

    [Fact]
    public void Defer_Should_RunKeyStepInMainPlan_When_OuterAndInnerConditionsDiffer()
    {
        // arrange
        var schema = ComposeTopology(Topology.OneHopKey);

        // act
        var plan = PlanOperation(schema, GetOperation(MutationShape.OuterAndInnerIfVariables));

        // assert
        MatchSnapshot(plan);
    }

    [Theory]
    [InlineData(Topology.DirectKey, MutationShape.AnchorOnMutationResult, "$.create")]
    [InlineData(Topology.OneHopKey, MutationShape.NestedTotalFirst, "$.create.user")]
    [InlineData(Topology.TwoHopKey, MutationShape.NestedTotalFirst, "$.create.user")]
    [InlineData(Topology.DirectKey, MutationShape.RootDefer, "$")]
    [InlineData(Topology.DirectKey, MutationShape.ThreeLevels, "$.create.user")]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.Nested, "$.create.user")]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.Sibling, "$.create.user")]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.NamedFragments, "$.create.user")]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.IfVariable, "$.create.user")]
    [InlineData(Topology.NameOnMutationSubgraph, MutationShape.TwoRootFields, "$.a.user")]
    public void Defer_Should_RefuseShape_When_DeferredFieldWouldNeedSecondMutationCall(
        Topology topology,
        MutationShape shape,
        string expectedPath)
    {
        // arrange
        var schema = ComposeTopology(topology);

        // act
        var error = Assert.Throws<DeferredMutationLookupRequiredException>(
            () => PlanOperation(schema, GetOperation(shape)));

        // assert
        Assert.Equal(expectedPath, error.Path.ToString());
    }

    [Fact]
    public void EnsureNoMutationStepInIncrementalPlan_Should_Throw_When_IncrementalPlanHoldsMutationStep()
    {
        // arrange
        var schema = ComposeTopology(Topology.DirectKey);
        var planner = CreatePlanner(schema);
        var operation = (OperationDefinitionNode)Utf8GraphQLParser.Parse(
            "mutation { create { user { id } } }").Definitions[0];
        var path = SelectionPath.Parse("$.create.user");
        var mutationStep = new OperationPlanStep
        {
            Id = 1,
            Definition = operation,
            Type = schema.MutationType!,
            RootSelectionSetId = 0,
            SelectionSets = [],
            SchemaName = "a",
            Target = SelectionPath.Root,
            Source = SelectionPath.Root
        };
        var descriptor = new IncrementalPlanDescriptor([], operation, path, null);

        // act
        var error = Assert.Throws<DeferredMutationLookupRequiredException>(
            () => planner.EnsureNoMutationStepInIncrementalPlan(descriptor, [mutationStep]));

        // assert
        Assert.Equal("$.create.user", error.Path.ToString());
        Assert.Equal("User", error.TypeName);
    }

    [Fact]
    public void EnsureNoMutationStepInIncrementalPlan_Should_NotThrow_When_IncrementalPlanHoldsOnlyQuerySteps()
    {
        // arrange
        var schema = ComposeTopology(Topology.DirectKey);
        var planner = CreatePlanner(schema);
        var operation = (OperationDefinitionNode)Utf8GraphQLParser.Parse(
            "mutation { create { user { id } } }").Definitions[0];
        var queryOperation = (OperationDefinitionNode)Utf8GraphQLParser.Parse(
            "query { userById(id: 1) { total } }").Definitions[0];
        var queryStep = new OperationPlanStep
        {
            Id = 1,
            Definition = queryOperation,
            Type = schema.QueryType,
            RootSelectionSetId = 0,
            SelectionSets = [],
            SchemaName = "b",
            Target = SelectionPath.Parse("$.create.user"),
            Source = SelectionPath.Root
        };
        var descriptor = new IncrementalPlanDescriptor([], operation, SelectionPath.Parse("$.create.user"), null);

        // act
        var error = Record.Exception(
            () => planner.EnsureNoMutationStepInIncrementalPlan(descriptor, [queryStep]));

        // assert
        Assert.Null(error);
    }

    private static string DescribeNodes(OperationPlan plan)
    {
        var plans = new List<string>
        {
            Describe(plan.AllNodes)
        };

        foreach (var incrementalPlan in plan.IncrementalPlans)
        {
            plans.Add(Describe(incrementalPlan.AllNodes));
        }

        return string.Join(" | ", plans);

        static string Describe(ImmutableArray<ExecutionNode> nodes)
            => string.Join(
                ",",
                nodes.OfType<OperationExecutionNode>().Select(t => $"{t.Operation.Type}/{t.SchemaName}"));
    }

    private static string GetOperation(MutationShape shape)
        => shape switch
        {
            MutationShape.Nested =>
                """
                mutation {
                    create {
                        user {
                            ... @defer(label: "outer") {
                                name
                                ... @defer(label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.NestedTotalFirst =>
                """
                mutation {
                    create {
                        user {
                            ... @defer(label: "outer") {
                                total
                                ... @defer(label: "inner") { name }
                            }
                        }
                    }
                }
                """,
            MutationShape.Sibling =>
                """
                mutation {
                    create {
                        user {
                            ... @defer(label: "a") { name }
                            ... @defer(label: "b") { total }
                        }
                    }
                }
                """,
            MutationShape.NamedFragments =>
                """
                mutation {
                    create {
                        user {
                            ...Outer @defer(label: "outer")
                        }
                    }
                }

                fragment Outer on User {
                    name
                    ...Inner @defer(label: "inner")
                }

                fragment Inner on User {
                    total
                }
                """,
            MutationShape.IfVariable =>
                """
                mutation($defer: Boolean!) {
                    create {
                        user {
                            ... @defer(if: $defer, label: "outer") {
                                name
                                ... @defer(if: $defer, label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.InnerIfVariable =>
                """
                mutation($defer: Boolean!) {
                    create {
                        user {
                            ... @defer(label: "outer") {
                                name
                                ... @defer(if: $defer, label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.SingleIfVariable =>
                """
                mutation($defer: Boolean!) {
                    create {
                        user {
                            ... @defer(if: $defer) { total }
                        }
                    }
                }
                """,
            MutationShape.OuterIfVariable =>
                """
                mutation($defer: Boolean!) {
                    create {
                        user {
                            ... @defer(if: $defer, label: "outer") {
                                name
                                ... @defer(label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.OuterAndInnerIfVariables =>
                """
                mutation($outer: Boolean!, $inner: Boolean!) {
                    create {
                        user {
                            ... @defer(if: $outer, label: "outer") {
                                name
                                ... @defer(if: $inner, label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.TwoRootFields =>
                """
                mutation {
                    a: create {
                        user {
                            ... @defer(label: "outer") {
                                name
                                ... @defer(label: "inner") { total }
                            }
                        }
                    }
                    b: create {
                        user {
                            ... @defer(label: "other") { name }
                        }
                    }
                }
                """,
            MutationShape.ListField =>
                """
                mutation {
                    createMany {
                        user {
                            ... @defer(label: "outer") {
                                name
                                ... @defer(label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.AnchorOnMutationResult =>
                """
                mutation {
                    create {
                        ... @defer(label: "outer") {
                            user {
                                name
                                ... @defer(label: "inner") { total }
                            }
                        }
                    }
                }
                """,
            MutationShape.RootDefer =>
                """
                mutation {
                    ... @defer(label: "outer") {
                        create {
                            user { name }
                        }
                    }
                }
                """,
            MutationShape.ThreeLevels =>
                """
                mutation {
                    create {
                        user {
                            ... @defer(label: "first") {
                                id
                                ... @defer(label: "second") {
                                    name
                                    ... @defer(label: "third") { total }
                                }
                            }
                        }
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

    private static OperationPlanner CreatePlanner(FusionSchemaDefinition schema)
    {
        var pool = new DefaultObjectPool<OrderedDictionary<string, List<FieldSelectionNode>>>(
            new DefaultPooledObjectPolicy<OrderedDictionary<string, List<FieldSelectionNode>>>());

        return new OperationPlanner(schema, new OperationCompiler(schema, pool));
    }

    private static FusionSchemaDefinition ComposeTopology(Topology topology)
    {
        const string mutationSchema =
            """
            # name: a
            type Query {
                ping: String
            }

            type Mutation {
                create: CreateResult!
                createMany: [CreateResult!]!
            }

            type CreateResult {
                user: User!
            }

            type User @key(fields: "id") {
                id: ID!
            }
            """;

        const string namedMutationSchema =
            """
            # name: a
            type Query {
                ping: String
                userById(id: ID!): User @lookup @internal
            }

            type Mutation {
                create: CreateResult!
                createMany: [CreateResult!]!
            }

            type CreateResult {
                user: User!
            }

            type User @key(fields: "id") {
                id: ID!
                name: String!
            }
            """;

        const string nameSchema =
            """
            # name: e
            type Query {
                userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
                id: ID!
                name: String!
            }
            """;

        const string totalByIdSchema =
            """
            # name: b
            type Query {
                userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
                id: ID!
                total: Int!
            }
            """;

        const string totalBySkuSchema =
            """
            # name: b
            type Query {
                userBySku(sku: String!): User @lookup @internal
            }

            type User @key(fields: "sku") {
                sku: String!
                total: Int!
            }
            """;

        const string skuByIdSchema =
            """
            # name: c
            type Query {
                userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
                id: ID!
                sku: String! @shareable
            }
            """;

        const string codeByIdSchema =
            """
            # name: c
            type Query {
                userById(id: ID!): User @lookup @internal
            }

            type User @key(fields: "id") {
                id: ID!
                code: String! @shareable
            }
            """;

        const string skuByCodeSchema =
            """
            # name: d
            type Query {
                userByCode(code: String!): User @lookup @internal
            }

            type User @key(fields: "code") {
                code: String!
                sku: String! @shareable
            }
            """;

        return topology switch
        {
            Topology.DirectKey => ComposeSchema(mutationSchema, totalByIdSchema, nameSchema),
            Topology.OneHopKey => ComposeSchema(mutationSchema, totalBySkuSchema, skuByIdSchema, nameSchema),
            Topology.TwoHopKey => ComposeSchema(
                mutationSchema,
                totalBySkuSchema,
                codeByIdSchema,
                skuByCodeSchema,
                nameSchema),
            Topology.NameOnMutationSubgraph => ComposeSchema(namedMutationSchema, totalByIdSchema),
            _ => throw new ArgumentOutOfRangeException(nameof(topology))
        };
    }

    public enum Topology
    {
        DirectKey,
        OneHopKey,
        TwoHopKey,
        NameOnMutationSubgraph
    }

    public enum MutationShape
    {
        Nested,
        NestedTotalFirst,
        Sibling,
        NamedFragments,
        IfVariable,
        InnerIfVariable,
        SingleIfVariable,
        OuterIfVariable,
        OuterAndInnerIfVariables,
        TwoRootFields,
        ListField,
        AnchorOnMutationResult,
        RootDefer,
        ThreeLevels
    }
}
