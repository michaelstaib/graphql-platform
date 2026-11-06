using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.Processing;

[Collection(DeferUsageSetTableCollection.Name)]
public class DeferUsageSetTableTests
{
    // a memory arena page of measurement noise stays below this; one allocation per item over 4080 items does not.
    private const long Tolerance = 32 * 1024;

    [Fact]
    public void GetActive_Should_ReturnStaticSet_When_AllUsagesAreActive()
    {
        // arrange
        var operation = Compile("""{ ... @defer(label: "A") { a } }""");
        var a = GetSelection(operation.RootSelectionSet, "a");
        var table = new DeferUsageSetTable(operation, CreateFlags(operation));

        // act
        var active = table.GetActive(a);

        // assert
        Assert.Same(a.DeferUsageSet, active);
    }

    [Fact]
    public void GetActive_Should_ReturnNull_When_NoUsageInTheChainIsActive()
    {
        // arrange
        var operation = Compile("""query($f: Boolean!) { ... @defer(label: "A", if: $f) { a } }""");
        var a = GetSelection(operation.RootSelectionSet, "a");
        var table = new DeferUsageSetTable(operation, CreateFlags(operation, ("f", false)));

        // act
        var first = table.GetActive(a);
        var second = table.GetActive(a);

        // assert
        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public void GetActive_Should_FoldIntoTheParent_When_UsageIsDisabled()
    {
        // arrange
        var operation = Compile(
            """
            query($c: Boolean!) {
              ... @defer(label: "A") { a }
              ... @defer(label: "B") { b ... @defer(label: "C", if: $c) { a } }
            }
            """);
        var a = GetSelection(operation.RootSelectionSet, "a");
        var table = new DeferUsageSetTable(operation, CreateFlags(operation, ("c", false)));

        // act
        var first = table.GetActive(a);
        var second = table.GetActive(a);

        // assert
        Format(a.DeferUsageSet).MatchInlineSnapshot("A, C");
        Format(first).MatchInlineSnapshot("A, B");
        Assert.Equal(-1, first!.Index);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetActive_Should_RemoveUsageBelowAnotherUsage_When_FoldedUsagesAreNested()
    {
        // arrange
        var operation = Compile(
            """
            query($f: Boolean!) {
              ... @defer(label: "Q") {
                ... @defer(label: "P") { ... @defer(label: "X", if: $f) { a } }
                ... @defer(label: "Y", if: $f) { a }
              }
            }
            """);
        var a = GetSelection(operation.RootSelectionSet, "a");
        var table = new DeferUsageSetTable(operation, CreateFlags(operation, ("f", false)));

        // act
        var active = table.GetActive(a);

        // assert
        Format(a.DeferUsageSet).MatchInlineSnapshot("X, Y");
        Format(active).MatchInlineSnapshot("Q");
    }

    [Fact]
    public void GetActive_Should_ReturnOneInstance_When_DifferentSetsResolveToEqualSets()
    {
        // arrange
        var operation = Compile(
            """
            query($f: Boolean!) {
              ... @defer(label: "A") { a ... @defer(label: "C", if: $f) { b } }
            }
            """);
        var a = GetSelection(operation.RootSelectionSet, "a");
        var b = GetSelection(operation.RootSelectionSet, "b");
        var table = new DeferUsageSetTable(operation, CreateFlags(operation, ("f", false)));

        // act
        var activeB = table.GetActive(b);
        var activeA = table.GetActive(a);

        // assert
        Format(activeB).MatchInlineSnapshot("A");
        Assert.NotSame(a.DeferUsageSet, b.DeferUsageSet);
        Assert.Same(activeB, activeA);
    }

    [Fact]
    public void GetActive_Should_ResolveSetOfALaterCompile_When_TableWasCreatedBeforeTheCompile()
    {
        // arrange
        var operation = Compile(
            """
            {
              ... @defer(label: "A") { a }
              obj { ... @defer(label: "B") { x } }
            }
            """);
        var a = GetSelection(operation.RootSelectionSet, "a");
        var table = new DeferUsageSetTable(operation, CreateFlags(operation));
        var activeBefore = table.GetActive(a);
        var obj = GetSelection(operation.RootSelectionSet, "obj");

        // act
        var x = GetSelection(operation.GetSelectionSet(obj), "x");
        var activeX = table.GetActive(x);

        // assert
        Assert.Equal(1, x.DeferUsageSet!.Index);
        Assert.Same(x.DeferUsageSet, activeX);
        Assert.Same(activeBefore, table.GetActive(a));
    }

    [Fact]
    public async Task Execute_Should_NotAllocatePerObject_When_DeferIsDisabledOverAList()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var small = await MeasureDifferenceAsync(executor, count: 16, alternate: false);
        var large = await MeasureDifferenceAsync(executor, count: 4096, alternate: false);

        // assert
        Assert.True(
            large - small < Tolerance,
            $"The deferred operation allocates {small} bytes more at 16 items and {large} bytes more at 4096 items.");
    }

    [Fact]
    public async Task Execute_Should_NotAllocatePerObject_When_DeferConditionAlternatesAcrossRequests()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var small = await MeasureDifferenceAsync(executor, count: 16, alternate: true);
        var large = await MeasureDifferenceAsync(executor, count: 4096, alternate: true);

        // assert
        Assert.True(
            large - small < Tolerance,
            $"The deferred operation allocates {small} bytes more at 16 items and {large} bytes more at 4096 items.");
    }

    // the bytes a request with a disabled defer allocates more than the same request without defer;
    // when alternating, a request with the defer enabled runs right before each measured request.
    private static async Task<long> MeasureDifferenceAsync(IRequestExecutor executor, int count, bool alternate)
    {
        const string deferred =
            """
            query($n: Int!, $v: Boolean!) { items(count: $n) { id ... @defer(if: $v) { name } } }
            """;
        const string plain =
            """
            query($n: Int!) { items(count: $n) { id name } }
            """;

        await ExecuteAsync(executor, deferred, count, deferValue: true);
        await ExecuteAsync(executor, deferred, count, deferValue: false);
        await ExecuteAsync(executor, plain, count, deferValue: null);

        // the smallest measurement of each operation is the one least disturbed by other work in the process.
        var deferredBytes = long.MaxValue;
        var plainBytes = long.MaxValue;

        for (var i = 0; i < 7; i++)
        {
            if (alternate)
            {
                await ExecuteAsync(executor, deferred, count, deferValue: true);
            }

            deferredBytes = Math.Min(deferredBytes, await MeasureAsync(executor, deferred, count, deferValue: false));
            plainBytes = Math.Min(plainBytes, await MeasureAsync(executor, plain, count, deferValue: null));
        }

        return deferredBytes - plainBytes;
    }

    private static async Task<long> MeasureAsync(IRequestExecutor executor, string query, int count, bool? deferValue)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        await ExecuteAsync(executor, query, count, deferValue);
        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    private static async Task ExecuteAsync(IRequestExecutor executor, string query, int count, bool? deferValue)
    {
        var request = OperationRequestBuilder.New()
            .SetDocument(query)
            .SetVariableValues(
                deferValue is { } value
                    ? new Dictionary<string, object?> { ["n"] = count, ["v"] = value }
                    : new Dictionary<string, object?> { ["n"] = count })
            .Build();

        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        if (result is IResponseStream stream)
        {
            await foreach (var payload in stream.ReadResultsAsync()
                .WithCancellation(TestContext.Current.CancellationToken))
            {
                await payload.DisposeAsync();
            }
        }

        await result.DisposeAsync();
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync()
        => await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<Query>()
            .ModifyOptions(o => o.EnableDefer = true)
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static Operation Compile(string sourceText)
        => OperationCompiler.Compile("opid", Utf8GraphQLParser.Parse(sourceText), CreateSchema());

    private static ConditionFlags CreateFlags(Operation operation, params (string Name, bool Value)[] variables)
    {
        var values = new TestVariables();

        foreach (var (name, value) in variables)
        {
            values[name] = new BooleanValueNode(value);
        }

        return operation.CreateDeferConditionFlags(values);
    }

    private static Selection GetSelection(SelectionSet selectionSet, string responseName)
    {
        Assert.True(selectionSet.TryGetSelection(responseName, out var selection));
        return selection;
    }

    private static string Format(DeferUsageSet? set)
        => set is null ? "null" : string.Join(", ", set.Usages.Select(u => u.Label));

    private static Schema CreateSchema()
        => SchemaBuilder.New()
            .AddDocumentFromString(
                """
                type Query {
                  a: String
                  b: String
                  obj: Obj
                }

                type Obj {
                  x: String
                }
                """)
            .Use(_ => _ => default)
            .Create();

    public class Query
    {
        public IEnumerable<Item> GetItems(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return new Item(i);
            }
        }
    }

    public sealed record Item(int Id)
    {
        public string Name => "name";
    }

    private sealed class TestVariables : IVariableValueCollection
    {
        private readonly Dictionary<string, IValueNode> _values = [];

        public IValueNode this[string name]
        {
            set => _values[name] = value;
        }

        public bool IsEmpty => _values.Count == 0;

        public T GetValue<T>(string name) where T : IValueNode
            => TryGetValue<T>(name, out var value) ? value : throw new KeyNotFoundException(name);

        public bool TryGetValue<T>(string name, [NotNullWhen(true)] out T? value) where T : IValueNode
        {
            if (_values.TryGetValue(name, out var node) && node is T casted)
            {
                value = casted;
                return true;
            }

            value = default;
            return false;
        }

        public IEnumerator<Execution.VariableValue> GetEnumerator()
            => throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DeferUsageSetTableCollection
{
    public const string Name = "DeferUsageSetTable";
}
