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

    [Fact]
    public async Task ReportError_Should_KeepTheErrorType_When_ConfigureIsAppliedToACustomError()
    {
        // arrange
        var errorTypes = new List<Type>();
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddErrorFilter(error =>
            {
                errorTypes.Add(error.GetType());
                return error;
            })
            .AddQueryType(d => d
                .Field("items")
                .Type<ListType<StringType>>()
                .Resolve(context =>
                {
                    context.ReportError(
                        new GraphQLException(new CustomError("item failed", null)),
                        error => error.SetPath(context.Path.Append(1)));
                    return new string?[] { "a", null, "c" };
                }))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync("{ items }", TestContext.Current.CancellationToken);

        // assert
        Assert.Equal([typeof(CustomError)], errorTypes);
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
    public async Task ReportError_Should_ApplyEveryChangeOfConfigure_When_ErrorIsACustomError()
    {
        // arrange
        var reported = new List<string>();
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddErrorFilter(error =>
            {
                reported.Add(
                    $"{error.GetType().Name} {error.Code} {error.Exception?.Message} {error.Path} "
                    + string.Join(",", (error.Locations ?? []).Select(l => $"{l.Line}:{l.Column}")));
                return error;
            })
            .AddQueryType(d => d
                .Field("items")
                .Type<ListType<StringType>>()
                .Resolve(context =>
                {
                    context.ReportError(
                        new GraphQLException(new CustomError("item failed", context.Path)),
                        error => error
                            .SetCode("CUSTOM")
                            .SetException(new InvalidOperationException("cause"))
                            .SetPath(context.Path.Append(1))
                            .AddLocation(new Location(2, 3)));
                    return new string?[] { "a" };
                }))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        await executor.ExecuteAsync("{ items }", TestContext.Current.CancellationToken);

        // assert
        reported.MatchInlineSnapshots(["CustomError CUSTOM cause items[1] 2:3"]);
    }

    private sealed record CustomError(
        string Message,
        Path? Path,
        IReadOnlyDictionary<string, object?>? Extensions = null) : IError
    {
        public string? Code => Extensions?.TryGetValue("code", out var code) == true ? code as string : null;

        public IReadOnlyList<Location>? Locations { get; init; }

        public Exception? Exception { get; init; }

        public IError WithMessage(string message) => this with { Message = message };

        public IError WithCode(string? code) => this;

        public IError WithPath(Path path) => this with { Path = path };

        public IError WithLocations(IReadOnlyList<Location> locations) => this with { Locations = locations };

        public IError WithExtensions(IReadOnlyDictionary<string, object?>? extensions)
            => this with { Extensions = extensions };

        public IError WithException(Exception? exception) => this with { Exception = exception };

        public IError RemoveExtension(string key) => this;

        public IError SetExtension(string key, object? value) => this;
    }
}
