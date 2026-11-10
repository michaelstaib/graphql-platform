using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.Pipeline;

public sealed class OperationNameResolutionTests
{
    [Fact]
    public async Task Execute_Should_RejectUndefinedOperationName_When_DocumentRanBefore()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d => d.Field("foo").Type<StringType>().Resolve(_ => "bar"))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        const string document = "query Foo { foo }";

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
                "foo": "bar"
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
}
