using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class ResolverCleanupTests
{
    [Fact]
    public async Task RegisterForCleanup_Should_RunRequestCleanupWhenTheResultIsDisposed_When_CleanAfterIsRequest()
    {
        // arrange
        List<string> log = [];
        var executor = await CreateExecutorAsync(
            context =>
            {
                context.RegisterForCleanup(() => Log(log, "resolver"), CleanAfter.Resolver);
                context.RegisterForCleanup(() => Log(log, "request"), CleanAfter.Request);
            });

        // act
        var result = await executor.ExecuteAsync("{ foo }", TestContext.Current.CancellationToken);
        var logBeforeDispose = log.ToArray();
        await result.DisposeAsync();
        await result.DisposeAsync();

        // assert
        Assert.Equal(["resolver"], logBeforeDispose);
        Assert.Equal(["resolver", "request"], log);
    }

    [Fact]
    public async Task RegisterForCleanup_Should_RunRequestCleanupOnce_When_TheRequestIsCancelled()
    {
        // arrange
        List<string> log = [];
        using var cancellation = new CancellationTokenSource();
        var executor = await CreateExecutorAsync(
            context =>
            {
                context.RegisterForCleanup(() => Log(log, "request"), CleanAfter.Request);
                cancellation.Cancel();
            });

        // act
        var result = await executor.ExecuteAsync("{ foo }", cancellation.Token);
        await result.DisposeAsync();

        // assert
        Assert.Equal(["request"], log);
    }

    [Fact]
    public async Task RegisterForCleanup_Should_RunRequestCleanupForEachContext_When_AVariableBatchIsCancelled()
    {
        // arrange
        List<string> log = [];
        var registrations = 0;
        using var cancellation = new CancellationTokenSource();
        var executor = await CreateExecutorAsync(
            context =>
            {
                context.RegisterForCleanup(() => Log(log, "request"), CleanAfter.Request);

                if (Interlocked.Increment(ref registrations) == 2)
                {
                    cancellation.Cancel();
                }
            });
        var request = OperationRequestBuilder.New()
            .SetDocument("query($n: Int) { foo(n: $n) }")
            .SetVariableValues("""[{ "n": 1 }, { "n": 2 }]""")
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, cancellation.Token);
        await result.DisposeAsync();

        // assert
        Assert.Equal(["request", "request"], log);
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync(Action<IMiddlewareContext> register)
        => await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor => descriptor
                    .Field("foo")
                    .Argument("n", a => a.Type<IntType>())
                    .Type<StringType>()
                    .Use(next => context =>
                    {
                        register(context);
                        return next(context);
                    })
                    .Resolve("bar"))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static ValueTask Log(List<string> log, string entry)
    {
        log.Add(entry);
        return default;
    }
}
