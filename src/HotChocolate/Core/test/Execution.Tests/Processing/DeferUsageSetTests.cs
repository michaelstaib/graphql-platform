using HotChocolate.Language;

namespace HotChocolate.Execution.Processing;

public class DeferUsageSetTests
{
    [Fact]
    public void Compile_Should_ShareOneSetInstance_When_SelectionsHaveTheSameDeferUsages()
    {
        // arrange
        var document = Utf8GraphQLParser.Parse(
            """
            {
              c
              ... @defer(label: "A") { a b }
            }
            """);

        // act
        var operation = OperationCompiler.Compile("opid", document, CreateSchema());

        // assert
        var a = GetSelection(operation.RootSelectionSet, "a");
        var b = GetSelection(operation.RootSelectionSet, "b");
        var c = GetSelection(operation.RootSelectionSet, "c");
        Assert.Same(a.DeferUsageSet, b.DeferUsageSet);
        Assert.Null(c.DeferUsageSet);
        Assert.Same(a.DeferUsageSet, Assert.Single(operation.DeferUsageSets));
        Assert.Equal(0, a.DeferUsageSet!.Index);
    }

    [Fact]
    public void Compile_Should_SortUsagesByOrdinal_When_NodesArriveInADifferentOrder()
    {
        // arrange
        // N is created by the child compile of obj, after A and B, but its x node is collected first.
        var document = Utf8GraphQLParser.Parse(
            """
            {
              ... @defer(label: "A") { obj { ... @defer(label: "N") { x } } }
              ... @defer(label: "B") { obj { x } }
            }
            """);
        var operation = OperationCompiler.Compile("opid", document, CreateSchema());
        var obj = GetSelection(operation.RootSelectionSet, "obj");

        // act
        var x = GetSelection(operation.GetSelectionSet(obj), "x");

        // assert
        Format(x.DeferUsageSet).MatchInlineSnapshot("B(1), N(2)");
    }

    [Fact]
    public void Compile_Should_ReuseUsageInstance_When_EqualUsagesAreCompiledLazily()
    {
        // arrange
        var document = Utf8GraphQLParser.Parse(
            """
            {
              first: obj { ... @defer { x } }
              second: obj { ... @defer { y } ... @defer(label: "B") { y } }
            }
            """);
        var operation = OperationCompiler.Compile("opid", document, CreateSchema());
        var first = GetSelection(operation.RootSelectionSet, "first");
        var second = GetSelection(operation.RootSelectionSet, "second");

        // act
        var x = GetSelection(operation.GetSelectionSet(first), "x");
        var y = GetSelection(operation.GetSelectionSet(second), "y");

        // assert
        Assert.Same(x.DeferUsageSet!.Usages[0], y.DeferUsageSet!.Usages[0]);
        Format(operation.DeferUsageSets).MatchInlineSnapshot(
            """
            (0)
            (0), B(1)
            """);
    }

    [Fact]
    public void Compile_Should_PublishNewSet_When_LazyCompileAddsASet()
    {
        // arrange
        var document = Utf8GraphQLParser.Parse(
            """
            {
              ... @defer(label: "A") { a }
              obj { ... @defer(label: "B") { x } }
            }
            """);
        var operation = OperationCompiler.Compile("opid", document, CreateSchema());
        var before = operation.DeferUsageSets;
        var obj = GetSelection(operation.RootSelectionSet, "obj");

        // act
        var x = GetSelection(operation.GetSelectionSet(obj), "x");

        // assert
        Format(before).MatchInlineSnapshot("A(0)");
        Format(operation.DeferUsageSets).MatchInlineSnapshot(
            """
            A(0)
            B(1)
            """);
        Assert.Equal(1, x.DeferUsageSet!.Index);
    }

    [Fact]
    public void Equals_Should_BeStructural_When_SetsHoldTheSameUsages()
    {
        // arrange
        var a = new DeferUsage("A", null, 0) { Ordinal = 0 };
        var b = new DeferUsage("B", null, 1) { Ordinal = 1 };
        var aCopy = new DeferUsage("A", null, 0) { Ordinal = 0 };

        // act
        var set = new DeferUsageSet([a, b], 0);
        var equal = new DeferUsageSet([aCopy, b], 1);
        var other = new DeferUsageSet([a], 2);

        // assert
        Assert.Equal(set, equal);
        Assert.Equal(set.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(set, other);
        Assert.NotEqual(a, new DeferUsage("A", null, 1));
    }

    [Theory]
    [InlineData(true, true, "B")]
    [InlineData(true, false, "A")]
    [InlineData(false, true, "B")]
    [InlineData(false, false, null)]
    public void FindActive_Should_ReturnNearestActiveUsage_When_ConditionsDiffer(
        bool outerEnabled,
        bool innerEnabled,
        string? expectedLabel)
    {
        // arrange
        var outer = new DeferUsage("A", null, 0) { Ordinal = 0 };
        var inner = new DeferUsage("B", outer, 1) { Ordinal = 1 };
        var flags = (outerEnabled ? 1ul : 0ul) | (innerEnabled ? 2ul : 0ul);

        // act
        var active = DeferUsage.FindActive(inner, flags, default);

        // assert
        Assert.Equal(expectedLabel, active?.Label);
    }

    [Fact]
    public void FindActive_Should_ReadOverflowWords_When_ConditionIndexIsWide()
    {
        // arrange
        var usage = new DeferUsage("A", null, 65) { Ordinal = 0 };

        // act
        var enabled = DeferUsage.FindActive(usage, 0ul, [2ul]);
        var disabled = DeferUsage.FindActive(usage, ulong.MaxValue, [0ul]);

        // assert
        Assert.Same(usage, enabled);
        Assert.Null(disabled);
    }

    [Fact]
    public void DeferUsageSetCount_Should_CountDistinctSets_When_SelectionsShareSets()
    {
        // arrange
        var document = Utf8GraphQLParser.Parse(
            """
            {
              c
              ... @defer(label: "A") { a b }
              ... @defer(label: "B") { b2: b obj { x } }
              ... @defer(label: "C") { obj { y } }
            }
            """);

        // act
        var operation = OperationCompiler.Compile("opid", document, CreateSchema());

        // assert
        Assert.Equal(3, operation.RootSelectionSet.DeferUsageSetCount);
    }

    private static Selection GetSelection(SelectionSet selectionSet, string responseName)
    {
        Assert.True(selectionSet.TryGetSelection(responseName, out var selection));
        return selection;
    }

    private static string Format(DeferUsageSet? set)
        => set is null
            ? "null"
            : string.Join(", ", set.Usages.Select(u => $"{u.Label}({u.Ordinal})"));

    private static string Format(IEnumerable<DeferUsageSet> sets)
        => string.Join(Environment.NewLine, sets.Select(Format));

    private static Schema CreateSchema()
        => SchemaBuilder.New()
            .AddDocumentFromString(
                """
                type Query {
                  a: String
                  b: String
                  c: String
                  obj: Obj
                }

                type Obj {
                  x: String
                  y: String
                  child: Obj
                }
                """)
            .Use(_ => _ => default)
            .Create();
}
