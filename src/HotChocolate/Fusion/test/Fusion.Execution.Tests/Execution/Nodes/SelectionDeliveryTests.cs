using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Fusion.Planning;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Execution.Nodes;

public class SelectionDeliveryTests : FusionTestBase
{
    private static readonly FusionSchemaDefinition s_schema = ComposeSchema(
        """
        # name: a
        type Query {
            product(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            name: String!
            nick: String!
        }

        scalar Boolean
        """,
        """
        # name: b
        type Query {
            productById(id: ID!): Product @lookup
        }

        type Product @key(fields: "id") {
            id: ID!
            price: Int!
        }
        """);

    [Fact]
    public void ResolveDelivery_Should_DeliverNameInitialAndPriceInGroup_When_IncludeIsTrueAndDeferIsTrue()
    {
        // arrange
        var operation = Compile(
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") {
                        name
                        price
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["x"] = true, ["d"] = true });

        // act
        var delivery = Describe(operation, variableValues, "name", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Initial",
            "price: Groups(L)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_DeliverNameAndPriceInitial_When_IncludeIsTrueAndDeferIsFalse()
    {
        // arrange
        var operation = Compile(
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") {
                        name
                        price
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["x"] = true, ["d"] = false });

        // act
        var delivery = Describe(operation, variableValues, "name", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Initial",
            "price: Initial"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_DeliverNameAndPriceInGroup_When_IncludeIsFalseAndDeferIsTrue()
    {
        // arrange
        var operation = Compile(
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") {
                        name
                        price
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["x"] = false, ["d"] = true });

        // act
        var delivery = Describe(operation, variableValues, "name", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Groups(L)",
            "price: Groups(L)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_DeliverNameAndPriceInitial_When_IncludeIsFalseAndDeferIsFalse()
    {
        // arrange
        var operation = Compile(
            """
            query($x: Boolean!, $d: Boolean!) {
                product(id: "1") {
                    name @include(if: $x)
                    ... @defer(if: $d, label: "L") {
                        name
                        price
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["x"] = false, ["d"] = false });

        // act
        var delivery = Describe(operation, variableValues, "name", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Initial",
            "price: Initial"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnAbsent_When_ConditionalSelectionIsExcluded()
    {
        // arrange
        var operation = Compile(
            """
            query($x: Boolean!) {
                product(id: "1") {
                    name
                    price @include(if: $x)
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["x"] = false });

        // act
        var delivery = Describe(operation, variableValues, "name", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Initial",
            "price: Absent"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnEnclosingGroup_When_NestedDeferIsDisabled()
    {
        // arrange
        var operation = Compile(
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "outer") {
                        ... @defer(if: $b, label: "inner") {
                            price
                        }
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["a"] = true, ["b"] = false });

        // act
        var delivery = Describe(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "price: Groups(outer)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnInnerGroup_When_NestedDeferIsEnabled()
    {
        // arrange
        var operation = Compile(
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "outer") {
                        ... @defer(if: $b, label: "inner") {
                            price
                        }
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["a"] = true, ["b"] = true });

        // act
        var delivery = Describe(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "price: Groups(inner)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnBothGroups_When_FieldIsInTwoSiblingGroups()
    {
        // arrange
        var operation = Compile(
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "A") {
                        price
                    }
                    ... @defer(if: $b, label: "B") {
                        price
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["a"] = true, ["b"] = true });

        // act
        var delivery = Describe(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "price: Groups(A, B)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnInitial_When_OneOfTwoSiblingGroupsIsDisabled()
    {
        // arrange
        var operation = Compile(
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $a, label: "A") {
                        price
                    }
                    ... @defer(if: $b, label: "B") {
                        price
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["a"] = true, ["b"] = false });

        // act
        var delivery = Describe(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "price: Initial"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnGroupB_When_ConditionalNameInGroupCIsSkipped()
    {
        // arrange
        var operation = Compile(
            """
            query($c: Boolean!, $b: Boolean!, $s: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $c, label: "c") {
                        name @skip(if: $s)
                    }
                    ... @defer(if: $b, label: "b") {
                        name
                        nick
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["c"] = false, ["b"] = true, ["s"] = true });

        // act
        var delivery = Describe(operation, variableValues, "name", "nick");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Groups(b)",
            "nick: Groups(b)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnGroupC_When_ConditionalNameInGroupBIsSkipped()
    {
        // arrange
        var operation = Compile(
            """
            query($c: Boolean!, $b: Boolean!, $s: Boolean!) {
                product(id: "1") {
                    ... @defer(if: $c, label: "c") {
                        name
                    }
                    ... @defer(if: $b, label: "b") {
                        name @skip(if: $s)
                        nick
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["c"] = true, ["b"] = false, ["s"] = true });

        // act
        var delivery = Describe(operation, variableValues, "name", "nick");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Groups(c)",
            "nick: Initial"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnOnlyOuterGroup_When_InnerGroupDeliversFieldToo()
    {
        // arrange
        var operation = Compile(
            """
            query($c: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    ... @defer(label: "outer") {
                        ... @defer(if: $c, label: "c") {
                            name
                        }
                        ... @defer(if: $b, label: "b") {
                            name
                            nick
                        }
                    }
                }
            }
            """);
        var variableValues = CreateVariableValues(
            operation,
            new Dictionary<string, bool> { ["c"] = false, ["b"] = true });

        // act
        var delivery = Describe(operation, variableValues, "name", "nick");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "name: Groups(outer)",
            "nick: Groups(b)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ResolveEachSwitchOnItsOwn_When_TwentyNineSiblingSwitchesAreMixed()
    {
        // arrange
        var operation = Compile(CreateSwitchDocument(29, static i => $"f{i}: price"));
        var variableValues = CreateVariableValues(operation, CreateSwitchValues(29, static i => i % 2 == 1));

        // act
        var delivery = Describe(operation, variableValues, CreateResponseNames(29));

        // assert
        delivery.MatchInlineSnapshots(
        [
            "f0: Initial",
            "f1: Groups(g1)",
            "f2: Initial",
            "f3: Groups(g3)",
            "f4: Initial",
            "f5: Groups(g5)",
            "f6: Initial",
            "f7: Groups(g7)",
            "f8: Initial",
            "f9: Groups(g9)",
            "f10: Initial",
            "f11: Groups(g11)",
            "f12: Initial",
            "f13: Groups(g13)",
            "f14: Initial",
            "f15: Groups(g15)",
            "f16: Initial",
            "f17: Groups(g17)",
            "f18: Initial",
            "f19: Groups(g19)",
            "f20: Initial",
            "f21: Groups(g21)",
            "f22: Initial",
            "f23: Groups(g23)",
            "f24: Initial",
            "f25: Groups(g25)",
            "f26: Initial",
            "f27: Groups(g27)",
            "f28: Initial"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnEveryGroup_When_FieldIsInTwentyNineSiblingGroupsAndAllSwitchesAreOn()
    {
        // arrange
        var operation = Compile(CreateSwitchDocument(29, static i => "price"));
        var variableValues = CreateVariableValues(operation, CreateSwitchValues(29, static i => true));

        // act
        var delivery = DescribeGroups(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "g0",
            "g1",
            "g2",
            "g3",
            "g4",
            "g5",
            "g6",
            "g7",
            "g8",
            "g9",
            "g10",
            "g11",
            "g12",
            "g13",
            "g14",
            "g15",
            "g16",
            "g17",
            "g18",
            "g19",
            "g20",
            "g21",
            "g22",
            "g23",
            "g24",
            "g25",
            "g26",
            "g27",
            "g28"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnInitial_When_FieldIsInTwentyNineSiblingGroupsAndOneSwitchIsOff()
    {
        // arrange
        var operation = Compile(CreateSwitchDocument(29, static i => "price"));
        var variableValues = CreateVariableValues(operation, CreateSwitchValues(29, static i => i != 7));

        // act
        var delivery = Describe(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "price: Initial"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ResolveEachSwitchOnItsOwn_When_SeventySiblingSwitchesAreMixed()
    {
        // arrange
        var operation = Compile(CreateSwitchDocument(70, static i => $"f{i}: price"));
        var variableValues = CreateVariableValues(operation, CreateSwitchValues(70, static i => i % 5 == 4));
        Assert.True(operation.HasWideIncludeFlags);
        Assert.True(operation.HasWideDeferFlags);

        // act
        var delivery = Describe(operation, variableValues, "f4", "f5", "f63", "f64", "f65", "f69");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "f4: Groups(g4)",
            "f5: Initial",
            "f63: Initial",
            "f64: Groups(g64)",
            "f65: Initial",
            "f69: Groups(g69)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnInitial_When_FieldIsInSeventySiblingGroupsAndLastSwitchIsOff()
    {
        // arrange
        var operation = Compile(CreateSwitchDocument(70, static i => "price"));
        var variableValues = CreateVariableValues(operation, CreateSwitchValues(70, static i => i != 69));
        Assert.True(operation.HasWideIncludeFlags);
        Assert.True(operation.HasWideDeferFlags);

        // act
        var delivery = Describe(operation, variableValues, "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "price: Initial"
        ]);
    }

    private static Operation Compile(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var normalized = new DocumentRewriter(s_schema).RewriteDocument(document, operationName: null);
        var operationDefinition = normalized.Definitions.OfType<OperationDefinitionNode>().First();
        var lowered = DeferLowering.Lower(operationDefinition, s_schema);
        var pool = new DefaultObjectPool<OrderedDictionary<string, List<FieldSelectionNode>>>(
            new FieldMapPooledObjectPolicy());

        return new OperationCompiler(s_schema, pool).Compile("1", "1", "1", lowered);
    }

    private static string CreateSwitchDocument(int count, Func<int, string> selection)
    {
        var variables = string.Join(", ", Enumerable.Range(0, count).Select(static i => $"$s{i}: Boolean!"));
        var fragments = string.Join(
            " ",
            Enumerable.Range(0, count).Select(i => $"... @defer(if: $s{i}, label: \"g{i}\") {{ {selection(i)} }}"));

        return $"query({variables}) {{ product(id: \"1\") {{ {fragments} }} }}";
    }

    private static string[] CreateResponseNames(int count)
        => Enumerable.Range(0, count).Select(static i => $"f{i}").ToArray();

    private static Dictionary<string, bool> CreateSwitchValues(int count, Func<int, bool> isOn)
        => Enumerable.Range(0, count).ToDictionary(static i => $"s{i}", isOn);

    private static VariableValueCollection CreateVariableValues(
        Operation operation,
        Dictionary<string, bool> values)
    {
        var booleanType = new NonNullType(operation.Schema.Types.GetType<IScalarTypeDefinition>("Boolean"));

        return new VariableValueCollection(
            values.ToDictionary(
                static t => t.Key,
                t => new VariableValue(t.Key, booleanType, new BooleanValueNode(t.Value))));
    }

    private static List<string> Describe(
        Operation operation,
        VariableValueCollection variableValues,
        params string[] responseNames)
    {
        var includeFlags = operation.CreateIncludeConditionFlags(variableValues);
        var deferFlags = operation.CreateDeferConditionFlags(variableValues);
        var selectionSet = operation.RootSelectionSet.Selections[0].GetSelectionSet()!;
        var lines = new List<string>();

        foreach (var responseName in responseNames)
        {
            Assert.True(selectionSet.TryGetSelection(responseName, out var selection));
            lines.Add($"{responseName}: {Format(selection.ResolveDelivery(includeFlags, deferFlags))}");
        }

        return lines;
    }

    private static List<string> DescribeGroups(
        Operation operation,
        VariableValueCollection variableValues,
        string responseName)
    {
        var includeFlags = operation.CreateIncludeConditionFlags(variableValues);
        var deferFlags = operation.CreateDeferConditionFlags(variableValues);
        var selectionSet = operation.RootSelectionSet.Selections[0].GetSelectionSet()!;
        Assert.True(selectionSet.TryGetSelection(responseName, out var selection));
        var resolution = selection.ResolveDelivery(includeFlags, deferFlags);

        return resolution.Kind is DeliveryKind.Groups
            ? ToLabels(resolution)
            : [resolution.Kind.ToString()];
    }

    private static string Format(DeliveryResolution resolution)
    {
        if (resolution.Kind is not DeliveryKind.Groups)
        {
            return resolution.Kind.ToString();
        }

        return $"Groups({string.Join(", ", ToLabels(resolution))})";
    }

    private static List<string> ToLabels(DeliveryResolution resolution)
    {
        var labels = new List<string>(resolution.Groups.Length);

        for (var i = 0; i < resolution.Groups.Length; i++)
        {
            labels.Add(resolution.Groups[i].Label!);
        }

        return labels;
    }
}
