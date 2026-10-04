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

    private static async Task<IRequestExecutor> CreateExecutorAsync(Action<IMiddlewareContext> register)
        => await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor => descriptor
                    .Field("foo")
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
