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

    [Fact]
    public void ResolveDelivery_Should_ReturnOneGroup_When_NestedSwitchableDefersHoldOnlyInnerField()
    {
        // arrange
        var operation = Compile(
            """
            query($a: Boolean!, $b: Boolean!) {
                product(id: "1") {
                    id
                    ... @defer(if: $a, label: "outer") {
                        ... @defer(if: $b, label: "inner") {
                            price
                        }
                    }
                }
            }
            """);

        // act
        var delivery = DescribeCombinations(operation, ["a", "b"], "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "a=F b=F: price=Initial",
            "a=F b=T: price=Groups(inner)",
            "a=T b=F: price=Groups(outer)",
            "a=T b=T: price=Groups(inner)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnOneGroupPerField_When_NestedDefersAreSwitchedByOneVariable()
    {
        // arrange
        var operation = Compile(
            """
            query($v: Boolean!) {
                product(id: "1") {
                    id
                    ... @defer(if: $v, label: "outer") {
                        name
                        ... @defer(if: $v, label: "inner") {
                            price
                        }
                    }
                }
            }
            """);

        // act
        var delivery = DescribeCombinations(operation, ["v"], "name", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "v=F: name=Initial price=Initial",
            "v=T: name=Groups(outer) price=Groups(inner)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnOneGroupPerField_When_ThreeSwitchableDefersAreNested()
    {
        // arrange
        var operation = Compile(
            """
            query($a: Boolean!, $b: Boolean!, $c: Boolean!) {
                product(id: "1") {
                    id
                    ... @defer(if: $a, label: "a") {
                        name
                        ... @defer(if: $b, label: "b") {
                            nick
                            ... @defer(if: $c, label: "c") {
                                price
                            }
                        }
                    }
                }
            }
            """);

        // act
        var delivery = DescribeCombinations(operation, ["a", "b", "c"], "name", "nick", "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "a=F b=F c=F: name=Initial nick=Initial price=Initial",
            "a=F b=F c=T: name=Initial nick=Initial price=Groups(c)",
            "a=F b=T c=F: name=Initial nick=Groups(b) price=Groups(b)",
            "a=F b=T c=T: name=Initial nick=Groups(b) price=Groups(c)",
            "a=T b=F c=F: name=Groups(a) nick=Groups(a) price=Groups(a)",
            "a=T b=F c=T: name=Groups(a) nick=Groups(a) price=Groups(c)",
            "a=T b=T c=F: name=Groups(a) nick=Groups(b) price=Groups(b)",
            "a=T b=T c=T: name=Groups(a) nick=Groups(b) price=Groups(c)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnGroupOnlyIfBothAreOn_When_IncludeIsOnOtherVariable()
    {
        // arrange
        var operation = Compile(
            """
            query($x: Boolean!, $v: Boolean!) {
                product(id: "1") {
                    id
                    ... @include(if: $x) @defer(if: $v, label: "L") {
                        price
                    }
                }
            }
            """);

        // act
        var delivery = DescribeCombinations(operation, ["x", "v"], "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "x=F v=F: price=Absent",
            "x=F v=T: price=Absent",
            "x=T v=F: price=Initial",
            "x=T v=T: price=Groups(L)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnGroupOnlyIfBothAreOn_When_IncludeIsOnOtherVariable_InputIsNotNormalized()
    {
        // arrange
        var operation = CompileNotNormalized(
            """
            query($x: Boolean!, $v: Boolean!) {
                product(id: "1") {
                    id
                    ... @include(if: $x) @defer(if: $v, label: "L") {
                        price
                    }
                }
            }
            """);

        // act
        var delivery = DescribeCombinations(operation, ["x", "v"], "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "x=F v=F: price=Absent",
            "x=F v=T: price=Absent",
            "x=T v=F: price=Initial",
            "x=T v=T: price=Groups(L)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnGroupOnlyIfVariableIsOn_When_IncludeUsesSameVariable_InputIsNotNormalized()
    {
        // arrange
        var operation = CompileNotNormalized(
            """
            query($v: Boolean!) {
                product(id: "1") {
                    id
                    ... @include(if: $v) @defer(if: $v, label: "L") {
                        price
                    }
                }
            }
            """);

        // act
        var delivery = DescribeCombinations(operation, ["v"], "price");

        // assert
        delivery.MatchInlineSnapshots(
        [
            "v=F: price=Absent",
            "v=T: price=Groups(L)"
        ]);
    }

    [Fact]
    public void ResolveDelivery_Should_ReturnOneGroupPerLabel_When_SwitchableDefersAreNested()
    {
        // arrange
        var shapes = new (string Name, string[] Variables, Operation Operation)[]
        {
            (
                "inner field only",
                ["a", "b"],
                Compile(
                    """
                    query($a: Boolean!, $b: Boolean!) {
                        product(id: "1") {
                            id
                            ... @defer(if: $a, label: "outer") {
                                ... @defer(if: $b, label: "inner") {
                                    price
                                }
                            }
                        }
                    }
                    """)),
            (
                "one variable on both levels",
                ["v"],
                Compile(
                    """
                    query($v: Boolean!) {
                        product(id: "1") {
                            id
                            ... @defer(if: $v, label: "outer") {
                                name
                                ... @defer(if: $v, label: "inner") {
                                    price
                                }
                            }
                        }
                    }
                    """)),
            (
                "three levels",
                ["a", "b", "c"],
                Compile(
                    """
                    query($a: Boolean!, $b: Boolean!, $c: Boolean!) {
                        product(id: "1") {
                            id
                            ... @defer(if: $a, label: "a") {
                                name
                                ... @defer(if: $b, label: "b") {
                                    nick
                                    ... @defer(if: $c, label: "c") {
                                        price
                                    }
                                }
                            }
                        }
                    }
                    """)),
            (
                "switchable defer at the query root",
                ["a", "b"],
                Compile(
                    """
                    query($a: Boolean!, $b: Boolean!) {
                        ... @defer(if: $a, label: "root") {
                            product(id: "1") {
                                id
                                ... @defer(if: $b, label: "inner") {
                                    price
                                }
                            }
                        }
                    }
                    """)),
            (
                "nested defer in two sibling groups",
                ["a", "b", "c", "d"],
                Compile(
                    """
                    query($a: Boolean!, $b: Boolean!, $c: Boolean!, $d: Boolean!) {
                        product(id: "1") {
                            id
                            ... @defer(if: $a, label: "first") {
                                name
                                ... @defer(if: $b, label: "firstInner") {
                                    price
                                }
                            }
                            ... @defer(if: $c, label: "second") {
                                nick
                                ... @defer(if: $d, label: "secondInner") {
                                    price
                                }
                            }
                        }
                    }
                    """)),
            (
                "include on another variable",
                ["x", "v"],
                CompileNotNormalized(
                    """
                    query($x: Boolean!, $v: Boolean!) {
                        product(id: "1") {
                            id
                            ... @include(if: $x) @defer(if: $v, label: "L") {
                                price
                            }
                        }
                    }
                    """))
        };

        // act
        var report = shapes
            .Select(static t => FindLabelViolations(t.Name, t.Operation, t.Variables))
            .ToList();

        // assert
        report.MatchInlineSnapshots(
        [
            "inner field only: 4 combinations, 0 violations",
            "one variable on both levels: 2 combinations, 0 violations",
            "three levels: 8 combinations, 0 violations",
            "switchable defer at the query root: 4 combinations, 0 violations",
            "nested defer in two sibling groups: 16 combinations, 0 violations",
            "include on another variable: 4 combinations, 0 violations"
        ]);
    }

    private static Operation Compile(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var normalized = new DocumentRewriter(s_schema).RewriteDocument(document, operationName: null);

        return Compile(normalized.Definitions.OfType<OperationDefinitionNode>().First());
    }

    private static Operation CompileNotNormalized(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);

        return Compile(document.Definitions.OfType<OperationDefinitionNode>().First());
    }

    private static Operation Compile(OperationDefinitionNode operationDefinition)
    {
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

    private static List<string> DescribeCombinations(
        Operation operation,
        string[] variables,
        params string[] responseNames)
    {
        var selectionSet = operation.RootSelectionSet.Selections[0].GetSelectionSet()!;
        var lines = new List<string>();

        foreach (var values in CreateCombinations(variables))
        {
            var variableValues = CreateVariableValues(operation, values);
            var includeFlags = operation.CreateIncludeConditionFlags(variableValues);
            var deferFlags = operation.CreateDeferConditionFlags(variableValues);
            var fields = new List<string>(responseNames.Length);

            foreach (var responseName in responseNames)
            {
                Assert.True(selectionSet.TryGetSelection(responseName, out var selection));
                fields.Add($"{responseName}={Format(selection.ResolveDelivery(includeFlags, deferFlags))}");
            }

            lines.Add($"{FormatCombination(variables, values)}: {string.Join(" ", fields)}");
        }

        return lines;
    }

    private static string FindLabelViolations(string name, Operation operation, string[] variables)
    {
        var violations = new List<string>();
        var combinations = CreateCombinations(variables);

        foreach (var values in combinations)
        {
            var variableValues = CreateVariableValues(operation, values);
            var includeFlags = operation.CreateIncludeConditionFlags(variableValues);
            var deferFlags = operation.CreateDeferConditionFlags(variableValues);
            var groups = new Dictionary<DeliveryGroup, List<string>>(ReferenceEqualityComparer.Instance);

            CollectActiveGroups(operation, operation.RootSelectionSet, string.Empty, includeFlags, deferFlags, groups);

            foreach (var label in groups.GroupBy(static t => t.Key.Label).Where(static g => g.Count() > 1))
            {
                var deliveries = label.Select(static t => $"[{string.Join(", ", t.Value)}]");
                violations.Add(
                    $"{FormatCombination(variables, values)}: label {label.Key} is on {label.Count()} groups "
                    + $"delivering {string.Join(" and ", deliveries)}");
            }
        }

        return string.Join(
            Environment.NewLine,
            violations.Prepend($"{name}: {combinations.Count} combinations, {violations.Count} violations"));
    }

    private static void CollectActiveGroups(
        Operation operation,
        SelectionSet selectionSet,
        string path,
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        Dictionary<DeliveryGroup, List<string>> groups)
    {
        foreach (var selection in selectionSet.Selections)
        {
            var resolution = selection.ResolveDelivery(includeFlags, deferFlags);
            var fieldPath = path + selection.ResponseName;

            if (resolution.Kind is DeliveryKind.Absent)
            {
                continue;
            }

            foreach (var group in resolution.Groups)
            {
                if (!groups.TryGetValue(group, out var paths))
                {
                    paths = [];
                    groups.Add(group, paths);
                }

                paths.Add(fieldPath);
            }

            if (selection.IsLeaf)
            {
                continue;
            }

            foreach (var possibleType in operation.GetPossibleTypes(selection))
            {
                CollectActiveGroups(
                    operation,
                    operation.GetSelectionSet(selection, possibleType),
                    fieldPath + ".",
                    includeFlags,
                    deferFlags,
                    groups);
            }
        }
    }

    private static List<Dictionary<string, bool>> CreateCombinations(string[] variables)
    {
        var combinations = new List<Dictionary<string, bool>>(1 << variables.Length);

        for (var mask = 0; mask < 1 << variables.Length; mask++)
        {
            var values = new Dictionary<string, bool>(variables.Length);

            for (var i = 0; i < variables.Length; i++)
            {
                values[variables[i]] = (mask >> (variables.Length - 1 - i) & 1) == 1;
            }

            combinations.Add(values);
        }

        return combinations;
    }

    private static string FormatCombination(string[] variables, Dictionary<string, bool> values)
        => string.Join(" ", variables.Select(t => $"{t}={(values[t] ? "T" : "F")}"));

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
