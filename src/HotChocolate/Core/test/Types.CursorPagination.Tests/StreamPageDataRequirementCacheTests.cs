using GreenDonut.Data;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Types.Pagination;

public class StreamPageDataRequirementCacheTests
{
    [Fact]
    public async Task Execute_Should_ResolveRequirementsPerRequest_When_IncludeAndSkipVariablesChange()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document =
            """
            query($nodes: Boolean!, $skipCount: Boolean!) {
              items {
                nodes @include(if: $nodes) { id }
                totalCount @skip(if: $skipCount)
              }
            }
            """;

        // act
        var results = new[]
        {
            await ExecuteAsync(executor, recorder, document, ("nodes", true), ("skipCount", false)),
            await ExecuteAsync(executor, recorder, document, ("nodes", false), ("skipCount", false)),
            await ExecuteAsync(executor, recorder, document, ("nodes", true), ("skipCount", true)),
            await ExecuteAsync(executor, recorder, document, ("nodes", false), ("skipCount", true)),
            await ExecuteAsync(executor, recorder, document, ("nodes", true), ("skipCount", false))
        };

        // assert
        results.MatchInlineSnapshot(
            """
            [
              "TotalCount, AllRows",
              "TotalCount",
              "AllRows",
              "None",
              "TotalCount, AllRows"
            ]
            """);
    }

    [Fact]
    public async Task Execute_Should_ResolveRequirementsPerRequest_When_StreamConditionVariableChanges()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document =
            """
            query($stream: Boolean!) {
              items {
                nodes @stream(if: $stream, initialCount: 1) { id }
                totalCount
              }
            }
            """;

        // act
        var results = new[]
        {
            await ExecuteAsync(executor, recorder, document, ("stream", true)),
            await ExecuteAsync(executor, recorder, document, ("stream", false)),
            await ExecuteAsync(executor, recorder, document, ("stream", true)),
            await ExecuteAsync(executor, recorder, document, ("stream", false))
        };

        // assert
        results.MatchInlineSnapshot(
            """
            [
              "TotalCount",
              "TotalCount, AllRows",
              "TotalCount",
              "TotalCount, AllRows"
            ]
            """);
    }

    [Fact]
    public async Task Execute_Should_ResolveRequirementsPerRequest_When_DeferConditionVariableChanges()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document =
            """
            query($defer: Boolean!) {
              items {
                totalCount
                ... @defer(if: $defer) { nodes { id } }
              }
            }
            """;

        // act
        var results = new[]
        {
            await ExecuteAsync(executor, recorder, document, ("defer", true)),
            await ExecuteAsync(executor, recorder, document, ("defer", false)),
            await ExecuteAsync(executor, recorder, document, ("defer", true)),
            await ExecuteAsync(executor, recorder, document, ("defer", false))
        };

        // assert
        results.MatchInlineSnapshot(
            """
            [
              "TotalCount",
              "TotalCount, AllRows",
              "TotalCount",
              "TotalCount, AllRows"
            ]
            """);
    }

    [Fact]
    public async Task Execute_Should_ResolveRequirementsPerRequest_When_PageInfoSelectionsAreConditional()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document =
            """
            query($start: Boolean!, $end: Boolean!) {
              items {
                pageInfo {
                  startCursor @include(if: $start)
                  endCursor @include(if: $end)
                }
              }
            }
            """;

        // act
        var results = new[]
        {
            await ExecuteAsync(executor, recorder, document, ("start", true), ("end", false)),
            await ExecuteAsync(executor, recorder, document, ("start", false), ("end", true)),
            await ExecuteAsync(executor, recorder, document, ("start", true), ("end", false)),
            await ExecuteAsync(executor, recorder, document, ("start", false), ("end", false))
        };

        // assert
        results.MatchInlineSnapshot(
            """
            [
              "FirstRow",
              "AllRows",
              "FirstRow",
              "None"
            ]
            """);
    }

    [Fact]
    public async Task Execute_Should_ResolveRequirementsPerRequest_When_MoreConditionCombinationsThanTheCacheHolds()
    {
        // arrange
        var (executor, recorder) = await CreateExecutorAsync();
        const string document =
            """
            query($nodes: Boolean!, $count: Boolean!, $pageInfo: Boolean!, $start: Boolean!, $end: Boolean!) {
              items {
                nodes @include(if: $nodes) { id }
                totalCount @include(if: $count)
                pageInfo @include(if: $pageInfo) {
                  startCursor @include(if: $start)
                  endCursor @include(if: $end)
                }
              }
            }
            """;
        List<string> mismatches = [];

        // act
        for (var pass = 0; pass < 2; pass++)
        {
            for (var bits = 0; bits < 32; bits++)
            {
                var nodes = (bits & 1) != 0;
                var count = (bits & 2) != 0;
                var pageInfo = (bits & 4) != 0;
                var start = (bits & 8) != 0;
                var end = (bits & 16) != 0;

                var expected = StreamPageDataRequirements.None;
                expected |= nodes ? StreamPageDataRequirements.AllRows : StreamPageDataRequirements.None;
                expected |= count ? StreamPageDataRequirements.TotalCount : StreamPageDataRequirements.None;
                expected |= pageInfo && start ? StreamPageDataRequirements.FirstRow : StreamPageDataRequirements.None;
                expected |= pageInfo && end ? StreamPageDataRequirements.AllRows : StreamPageDataRequirements.None;

                var actual = await ExecuteAsync(
                    executor,
                    recorder,
                    document,
                    ("nodes", nodes),
                    ("count", count),
                    ("pageInfo", pageInfo),
                    ("start", start),
                    ("end", end));

                if (actual != expected.ToString())
                {
                    mismatches.Add($"{pass}/{bits}: expected {expected}, was {actual}");
                }
            }
        }

        // assert
        Assert.Empty(mismatches);
    }

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
    }

    public sealed class Query
    {
        [UseConnection(IncludeTotalCount = true)]
        public RecordingConnection GetItems([Service] RequirementRecorder recorder)
            => new(StreamPage<Item>.Empty, recorder);
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
            CancellationToken cancellationToken)
        {
            _recorder.Requirements.Add(requirements.ToString());
            return ValueTask.CompletedTask;
        }

        ValueTask IStreamPageConnection.DisposeAsync() => _page.DisposeAsync();
    }

    public sealed class QueryType : ObjectType<Query>
    {
        protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
            => descriptor.Field(query => query.GetItems(default!)).AddPagingArguments();
    }

    public sealed class Item
    {
        public int Id { get; set; }
    }
}
