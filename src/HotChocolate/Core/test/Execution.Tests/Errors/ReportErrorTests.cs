using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.Errors;

public class ReportErrorTests
{
    [Fact]
    public async Task ReportError_Should_ApplyConfigure_When_ExceptionIsGraphQLException()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d => d
                .Field("items")
                .Type<ListType<StringType>>()
                .Resolve(context =>
                {
                    context.ReportError(
                        new GraphQLException("item failed"),
                        error => error.SetPath(context.Path.Append(1)));
                    return new string?[] { "a", null, "c" };
                }))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync("{ items }", TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "item failed",
                  "path": [
                    "items",
                    1
                  ]
                }
              ],
              "data": {
                "items": [
                  "a",
                  null,
                  "c"
                ]
              }
            }
            """);
    }

    [Fact]
    public async Task ReportError_Should_ApplyConfigure_When_ExceptionIsAggregateException()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d => d
                .Field("items")
                .Type<ListType<StringType>>()
                .Resolve(context =>
                {
                    context.ReportError(
                        new AggregateException(new GraphQLException("item failed")),
                        error => error.SetPath(context.Path.Append(1)));
                    return new string?[] { "a", null, "c" };
                }))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync("{ items }", TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "item failed",
                  "path": [
                    "items",
                    1
                  ]
                }
              ],
              "data": {
                "items": [
                  "a",
                  null,
                  "c"
                ]
              }
            }
            """);
    }
}
