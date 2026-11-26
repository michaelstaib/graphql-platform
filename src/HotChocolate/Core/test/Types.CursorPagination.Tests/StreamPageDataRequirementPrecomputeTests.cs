using GreenDonut.Data;
using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Types.Pagination;

public class StreamPageDataRequirementPrecomputeTests
{
    [Theory]
    [InlineData("items { nodes { id } }", "AllRows")]
    [InlineData("items { edges { cursor } }", "AllRows")]
    [InlineData("items { totalCount }", "TotalCount")]
    [InlineData("items { pageInfo { startCursor } }", "FirstRow")]
    [InlineData("items { pageInfo { hasNextPage } }", "AllRows")]
    [InlineData("items { pageInfo { hasPreviousPage hasNextPage } }", "FirstRow, AllRows")]
    [InlineData("items { totalCount pageInfo { startCursor } nodes { id } }", "TotalCount, FirstRow, AllRows")]
    [InlineData("items { pageInfo { __typename } __typename }", "None")]
    public async Task Execute_Should_StorePrecomputedRequirements_When_SelectionsHaveNoConditions(
        string selection,
        string expected)
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();

        // act
        var requirements = await ExecuteAsync(executor, recorder, $"{{ {selection} }}");

        // assert
        Assert.Equal(expected, requirements);
        Assert.Equal(expected, GetPrecomputed(recorder));
    }

    [Fact]
    public async Task Execute_Should_ReadThePrecomputedRequirements_When_TheOperationIsExecutedAgain()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document = "{ items { nodes { id } totalCount } }";
        await ExecuteAsync(executor, recorder, document);
        var features = recorder.Selection!.Features;
        features[typeof(StreamPageDataRequirements)] = StreamPageDataRequirements.FirstRow;

        // act
        var requirements = await ExecuteAsync(executor, recorder, document);

        // assert
        Assert.Equal("FirstRow", requirements);
    }

    [Theory]
    [InlineData("query($v: Boolean!) { ... @include(if: $v) { items { nodes { id } totalCount } } }")]
    [InlineData("query($v: Boolean!) { ... @defer(if: $v) { items { nodes { id } totalCount } } }")]
    public async Task Execute_Should_StorePrecomputedRequirements_When_OnlyTheConnectionIsConditional(
        string document)
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();

        // act
        var requirements = await ExecuteAsync(executor, recorder, document, ("v", true));

        // assert
        Assert.Equal(GetPrecomputed(recorder), requirements);
    }

    [Theory]
    [InlineData(
        "items { totalCount nodes @include(if: $v) { id } }",
        "TotalCount, AllRows",
        "TotalCount")]
    [InlineData(
        "items { totalCount nodes @skip(if: $v) { id } }",
        "TotalCount",
        "TotalCount, AllRows")]
    [InlineData(
        "items { totalCount ... @defer(if: $v) { nodes { id } } }",
        "TotalCount",
        "TotalCount, AllRows")]
    [InlineData(
        "items { totalCount nodes @stream(if: $v, initialCount: 1) { id } }",
        "TotalCount, rows: 1",
        "TotalCount, AllRows")]
    [InlineData(
        "items { totalCount ... @include(if: $v) { nodes { id } } }",
        "TotalCount, AllRows",
        "TotalCount")]
    [InlineData(
        "items { pageInfo @include(if: $v) { startCursor } }",
        "FirstRow",
        "None")]
    [InlineData(
        "items { pageInfo { endCursor @include(if: $v) } }",
        "AllRows",
        "None")]
    [InlineData(
        "items { pageInfo { endCursor @skip(if: $v) } }",
        "None",
        "AllRows")]
    [InlineData(
        "items { pageInfo { ... @defer(if: $v) { endCursor } } }",
        "None",
        "AllRows")]
    public async Task Execute_Should_ComputeRequirementsPerRequest_When_ARelevantSelectionHasACondition(
        string selection,
        string expectedWhenTrue,
        string expectedWhenFalse)
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        var document = $"query($v: Boolean!) {{ {selection} }}";

        // act
        var whenTrue = await ExecuteAsync(executor, recorder, document, ("v", true));
        var whenFalse = await ExecuteAsync(executor, recorder, document, ("v", false));
        var whenTrueAgain = await ExecuteAsync(executor, recorder, document, ("v", true));

        // assert
        Assert.Equal(expectedWhenTrue, whenTrue);
        Assert.Equal(expectedWhenFalse, whenFalse);
        Assert.Equal(expectedWhenTrue, whenTrueAgain);
        Assert.Null(GetPrecomputed(recorder));
    }

    [Theory]
    [InlineData("items { nodes @stream(initialCount: 2) { id } }", "None, rows: 2")]
    [InlineData("items { nodes @stream { id } }", "None")]
    [InlineData("items { edges @stream(initialCount: 1) { cursor } nodes @stream(initialCount: 3) { id } }", "None, rows: 3")]
    [InlineData("items { totalCount pageInfo { startCursor } nodes @stream(initialCount: 2) { id } }", "TotalCount, FirstRow, rows: 2")]
    [InlineData("items { nodes @stream(initialCount: 1) { id } ... @defer { pageInfo { hasNextPage } } }", "None, rows: 1")]
    [InlineData("items { nodes @stream(initialCount: 1) { id } pageInfo { ... @defer { endCursor } } }", "None, rows: 1")]
    [InlineData("items { edges @stream(initialCount: 1) { cursor } ... @defer { nodes @stream(initialCount: 2) { id } } }", "None, rows: 1")]
    public async Task Execute_Should_ReadTheInitialItemsOfTheStreams_When_TheStreamsAreHonored(
        string selection,
        string expected)
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();

        // act
        var requirements = await ExecuteAsync(executor, recorder, $"{{ {selection} }}");

        // assert
        Assert.Equal(expected, requirements);
    }

    [Theory]
    [InlineData("items { edges { cursor } nodes @stream(initialCount: 1) { id } }")]
    [InlineData("items { edges @stream(initialCount: 1) { cursor } ... @defer { nodes { id } } }")]
    [InlineData("items { nodes @stream(initialCount: 1) { id } pageInfo { hasNextPage } }")]
    [InlineData("items { nodes @stream(initialCount: 1) { id } pageInfo { endCursor } }")]
    public async Task Execute_Should_ReadAllRows_When_TheStreamsAreIgnored(string selection)
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();

        // act
        var requirements = await ExecuteAsync(executor, recorder, $"{{ {selection} }}");

        // assert
        Assert.Equal("AllRows", requirements);
    }

    [Fact]
    public async Task Execute_Should_StorePrecomputedRequirements_When_OnlyAnIrrelevantSelectionHasACondition()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document = "query($v: Boolean!) { items { nodes { id } __typename @include(if: $v) } }";

        // act
        var requirements = await ExecuteAsync(executor, recorder, document, ("v", false));

        // assert
        Assert.Equal("AllRows", requirements);
        Assert.Equal("AllRows", GetPrecomputed(recorder));
    }

    [Fact]
    public async Task Execute_Should_ComputeRequirementsPerRequest_When_TheConditionIsBeyondTheFirstWord()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const int conditionCount = 65;
        var variables = Enumerable.Range(0, conditionCount).Select(i => $"v{i}").ToArray();
        var declarations = string.Join(", ", variables.Select(name => $"${name}: Boolean!"));
        var rootSelections = string.Join(
            " ",
            variables.Take(conditionCount - 1).Select(name => $"{name}: __typename @include(if: ${name})"));
        var document =
            $$"""
            query({{declarations}}) {
              {{rootSelections}}
              items {
                totalCount
                nodes @include(if: ${{variables[^1]}}) { id }
              }
            }
            """;

        (string, bool)[] CreateVariables(bool last)
            => [.. variables.Take(conditionCount - 1).Select(name => (name, true)), (variables[^1], last)];

        // act
        var whenTrue = await ExecuteAsync(executor, recorder, document, CreateVariables(true));
        var whenFalse = await ExecuteAsync(executor, recorder, document, CreateVariables(false));

        // assert
        Assert.Equal("TotalCount, AllRows", whenTrue);
        Assert.Equal("TotalCount", whenFalse);
        Assert.Null(GetPrecomputed(recorder));
    }

    private static string? GetPrecomputed(RequirementRecorder recorder)
        => recorder.Selection!.Features.TryGet<StreamPageDataRequirements>(out var requirements)
            ? requirements.ToString()
            : null;

    private static async Task<string> ExecuteAsync(
        IRequestExecutor executor,
        RequirementRecorder recorder,
        string document,
        params (string Name, bool Value)[] variables)
    {
        recorder.Requirements.Clear();

        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New()
                .SetDocument(document)
                .SetVariableValues(variables.ToDictionary(t => t.Name, t => (object?)t.Value))
                .Build(),
            TestContext.Current.CancellationToken);

        if (result is ResponseStream stream)
        {
            await foreach (var payload in stream.ReadResultsAsync())
            {
                await payload.DisposeAsync();
            }
        }

        await result.DisposeAsync();

        return recorder.Requirements.Count == 0
            ? StreamPageDataRequirements.None.ToString()
            : recorder.Requirements.Single();
    }

    private static async Task<(IRequestExecutor Executor, RequirementRecorder Recorder)> CreateExecutorAsync()
    {
        var recorder = new RequirementRecorder();
        var executor = await new ServiceCollection()
            .AddSingleton(recorder)
            .AddGraphQL()
            .AddQueryType<QueryType>()
            .ModifyOptions(options =>
            {
                options.EnableDefer = true;
                options.EnableStream = true;
            })
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        return (executor, recorder);
    }

    public sealed class RequirementRecorder
    {
        public List<string> Requirements { get; } = [];

        public Selection? Selection { get; set; }
    }

    public sealed class Query
    {
        [UseConnection(IncludeTotalCount = true)]
        public RecordingConnection GetItems([Service] RequirementRecorder recorder, IResolverContext context)
        {
            recorder.Selection = context.Selection;
            return new RecordingConnection(StreamPage<Item>.Empty, recorder);
        }
    }

    public sealed class RecordingConnection : StreamPageConnection<Item>, IStreamPageConnection
    {
        private readonly StreamPage<Item> _page;
        private readonly RequirementRecorder _recorder;

        public RecordingConnection(StreamPage<Item> page, RequirementRecorder recorder)
            : base(page)
        {
            _page = page;
            _recorder = recorder;
        }

        ValueTask IStreamPageConnection.LoadRequirementsAsync(
            StreamPageDataRequirements requirements,
            int rowCount,
            CancellationToken cancellationToken)
        {
            _recorder.Requirements.Add(rowCount == 0 ? requirements.ToString() : $"{requirements}, rows: {rowCount}");
            return ValueTask.CompletedTask;
        }

        ValueTask IStreamPageConnection.DisposeAsync() => _page.DisposeAsync();
    }

    public sealed class QueryType : ObjectType<Query>
    {
        protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
            => descriptor.Field(query => query.GetItems(default!, default!)).AddPagingArguments();
    }

    public sealed class Item
    {
        public int Id { get; set; }
    }
}
