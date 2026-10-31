using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution.Nodes;

public sealed class OperationBatchExecutionNodeTests : FusionTestBase
{
    private const string SchemaA =
        """
        # name: a
        type Query {
          foos: [Foo]
          bars: [Bar]
        }

        type Foo @key(fields: "id") {
          id: ID!
        }

        type Bar @key(fields: "id") {
          id: ID!
        }
        """;

    private const string SchemaB =
        """
        # name: b
        type Query {
          fooById(id: ID! @is(field: "id")): Foo @lookup @internal
          barById(id: ID! @is(field: "id")): Bar @lookup @internal
        }

        type Foo @key(fields: "id") {
          id: ID!
          name: String
        }

        type Bar @key(fields: "id") {
          id: ID!
          title: String
        }
        """;

    private const string BatchOperation =
        """
        {
          foos {
            id
            name
          }
          bars {
            id
            title
          }
        }
        """;

    [Fact]
    public async Task ExecuteAsync_Should_ReuseAndClearRequestBuffer_When_BatchRunsOnPooledContext()
    {
        // arrange
        var client = new RecordingBatchClient();
        var executor = await CreateExecutorAsync(client);

        // act
        await ExecuteAsync(executor);
        await ExecuteAsync(executor);

        // assert
        Assert.Equal(new[] { 2, 2 }, client.Batches.Select(batch => batch.Count));
        Assert.Same(client.Batches[0].Buffer, client.Batches[1].Buffer);
        Assert.Equal(new[] { "b", "b" }, client.Batches[1].SchemaNames);
        Assert.All(client.Batches[1].Buffer, request => Assert.Null(request.Node));
    }

    private static async Task ExecuteAsync(IRequestExecutor executor)
    {
        await using var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocument(BatchOperation).Build(),
            TestContext.Current.CancellationToken);
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync(RecordingBatchClient client)
    {
        var services = new ServiceCollection();
        services.AddHttpClient();

        var builder = services
            .AddGraphQLGateway()
            .AddInMemoryConfiguration(ComposeSchemaDocument(SchemaA, SchemaB))
            .ModifyCostOptions(options => options.SkipAnalyzer = true);

        builder.Services.AddSingleton<ISourceSchemaClientFactory>(new RecordingClientFactory(client));

        FusionSetupUtilities.Configure(
            builder,
            setup =>
            {
                setup.ClientConfigurationModifiers.Add(_ => new RecordingClientConfiguration("a"));
                setup.ClientConfigurationModifiers.Add(_ => new RecordingClientConfiguration("b"));
            });

        return await services.BuildGatewayAsync(TestContext.Current.CancellationToken);
    }

    private sealed record RecordedBatch(
        SourceSchemaClientRequest[] Buffer,
        int Count,
        string[] SchemaNames);

    private sealed class RecordingBatchClient : ISourceSchemaClient
    {
        private static readonly byte[] s_rootResponse =
            """{"data":{"foos":[{"id":"1"}],"bars":[{"id":"2"}]}}"""u8.ToArray();

        public List<RecordedBatch> Batches { get; } = [];

        public SourceSchemaClientCapabilities Capabilities => SourceSchemaClientCapabilities.Default;

        public async IAsyncEnumerable<SourceSchemaResult> ExecuteAsync(
            OperationPlanContext context,
            SourceSchemaClientRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();

            var document = SourceResultDocument.Parse(
                context.MemorySource.GetNextArena(),
                s_rootResponse,
                s_rootResponse.Length);

            yield return new SourceSchemaResult(CompactPath.Root, document);
        }

        public async IAsyncEnumerable<SourceSchemaBatchResult> ExecuteBatchAsync(
            OperationPlanContext context,
            ImmutableArray<SourceSchemaClientRequest> requests,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Batches.Add(
                new RecordedBatch(
                    ImmutableCollectionsMarshal.AsArray(requests)!,
                    requests.Length,
                    [.. requests.Select(request => request.SchemaName)]));

            await Task.Yield();
            yield break;
        }

        public IAsyncEnumerable<SourceSchemaResult> SubscribeAsync(
            OperationPlanContext context,
            SourceSchemaClientRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingClientFactory(RecordingBatchClient client) : ISourceSchemaClientFactory
    {
        public bool CanHandle(ISourceSchemaClientConfiguration configuration)
            => configuration is RecordingClientConfiguration;

        public ISourceSchemaClient CreateClient(
            FusionSchemaDefinition schema,
            ISourceSchemaClientConfiguration configuration)
            => client;
    }

    private sealed class RecordingClientConfiguration(string name) : ISourceSchemaClientConfiguration
    {
        public string Name { get; } = name;

        public SupportedOperationType SupportedOperations { get; } = SupportedOperationType.All;
    }
}
