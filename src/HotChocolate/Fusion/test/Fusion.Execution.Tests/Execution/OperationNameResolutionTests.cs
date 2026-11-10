using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution;

public class OperationNameResolutionTests : FusionTestBase
{
    [Fact]
    public async Task Execute_Should_RejectUndefinedOperationName_When_DocumentRanBefore()
    {
        // arrange
        var executor = await CreateExecutorAsync();
        const string document = "query Foo { __typename }";

        // act
        var first = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocument(document).SetOperationName("Foo").Build(),
            TestContext.Current.CancellationToken);
        var second = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocument(document).SetOperationName("Bar").Build(),
            TestContext.Current.CancellationToken);

        // assert
        first.MatchInlineSnapshot(
            """
            {
              "data": {
                "__typename": "Query"
              }
            }
            """);
        second.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The specified operation `Bar` cannot be found.",
                  "locations": [
                    {
                      "line": 1,
                      "column": 1
                    }
                  ],
                  "extensions": {
                    "operationName": "Bar"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_ExecuteOperation_When_NameIsOmittedOrDefined()
    {
        // arrange
        var executor = await CreateExecutorAsync();
        const string document = "query Foo { __typename }";

        // act
        var named = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocument(document).SetOperationName("Foo").Build(),
            TestContext.Current.CancellationToken);
        var omitted = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocument(document).Build(),
            TestContext.Current.CancellationToken);

        // assert
        named.MatchInlineSnapshot(
            """
            {
              "data": {
                "__typename": "Query"
              }
            }
            """);
        omitted.MatchInlineSnapshot(
            """
            {
              "data": {
                "__typename": "Query"
              }
            }
            """);
    }

    private async Task<IRequestExecutor> CreateExecutorAsync()
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient();
        services
            .AddGraphQLGateway()
            .AddInMemoryConfiguration(
                ComposeSchemaDocument(
                    """
                    type Query {
                      field: String!
                    }
                    """));

        return await services.BuildServiceProvider().GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
