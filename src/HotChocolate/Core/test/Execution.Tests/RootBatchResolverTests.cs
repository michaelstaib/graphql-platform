using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class RootBatchResolverTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheOnlyRootFieldIsABatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ batchField }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"batchField":"batched-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ABatchFieldHasASiblingRootField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ plain batchField }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"plain":"plain","batchField":"batched-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheBatchFieldIsNested()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ foo { batchField } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"foo":{"batchField":"batched-1"}}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ARootBatchFieldHasANestedBatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "{ batchFoo { batchField } }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"batchFoo":{"batchField":"batched-1"}}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_AMutationHasOnlyBatchRootFields()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var json = await ExecuteAsync(executor, "mutation { first second }");

        // assert
        json.MatchInlineSnapshot(
            """
            {"data":{"first":"batched-1","second":"batched-1"}}
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_TheOnlyNonDeferredRootFieldIsABatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var payloads = await ExecuteIncrementalAsync(executor, "{ batchField ... @defer { plain } }");

        // assert
        Assert.Contains("\"batchField\":\"batched-1\"", payloads[0], StringComparison.Ordinal);
        Assert.Contains(payloads, p => p.Contains("\"plain\":\"plain\"", StringComparison.Ordinal));
        Assert.Contains("\"hasNext\":false", payloads[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ADeferredFragmentHasANestedBatchField()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var payloads = await ExecuteIncrementalAsync(executor, "{ plain ... @defer { foo { batchField } } }");

        // assert
        Assert.Contains("\"plain\":\"plain\"", payloads[0], StringComparison.Ordinal);
        Assert.Contains(payloads, p => p.Contains("\"foo\":{\"batchField\":\"batched-1\"}", StringComparison.Ordinal));
        Assert.Contains("\"hasNext\":false", payloads[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CompleteTheRequest_When_ASiblingTaskCompletesAfterTheTaskRegisteringTheBatch()
    {
        // arrange
        var executor = await CreateExecutorAsync();
        var results = new List<string>();

        // act
        for (var i = 0; i < 50; i++)
        {
            results.Add(await ExecuteAsync(executor, "{ task foo { batchField } }"));
        }

        // assert
        Assert.Equal(
            """{"data":{"task":"task","foo":{"batchField":"batched-1"}}}""",
            results.Distinct().Single());
    }

    private static Task<string> ExecuteAsync(IRequestExecutor executor, string query)
    {
        // the scheduler loop must not be able to block the test thread, so the request runs off it.
        return Task.Run(
                async () =>
                {
                    var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    return result.ToJson(withIndentations: false);
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static Task<string[]> ExecuteIncrementalAsync(IRequestExecutor executor, string query)
    {
        return Task.Run(
                async () =>
                {
                    await using var result = await executor.ExecuteAsync(
                        query,
                        TestContext.Current.CancellationToken);
                    var payloads = new List<string>();

                    await foreach (var payload in result
                        .ExpectResponseStream()
                        .ReadResultsAsync()
                        .WithCancellation(TestContext.Current.CancellationToken))
                    {
                        payloads.Add(payload.ToJson(withIndentations: false));
                        await payload.DisposeAsync();
                    }

                    return payloads.ToArray();
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("plain").Type<StringType>().Resolve("plain");
                d.Field("task").Type<StringType>().Resolve(async _ => { await Task.Yield(); return "task"; });
                d.Field("foo").Type<ObjectType<Foo>>().Resolve(new Foo());
                d.Field("batchField").Type<StringType>().ResolveBatch(ResolveBatch);
                d.Field("batchFoo").Type<ObjectType<Foo>>().ResolveBatch(ResolveFooBatch);
            })
            .AddMutationType(d =>
            {
                d.Name("Mutation");
                d.Field("first").Type<StringType>().ResolveBatch(ResolveBatch);
                d.Field("second").Type<StringType>().ResolveBatch(ResolveBatch);
            })
            .AddObjectType<Foo>(d => d.Field("batchField").Type<StringType>().ResolveBatch(ResolveBatch))
            .ModifyOptions(o => o.EnableDefer = true)
            .BuildRequestExecutorAsync();

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatch(IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok($"batched-{contexts.Count}");
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveFooBatch(IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok(new Foo());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    public sealed class Foo
    {
        public string Name => "foo";
    }
}
