using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Execution;

public class RequestContextPoolTests : FusionTestBase
{
    [Fact]
    public async Task Get_Should_ReturnPooledContexts_When_ContextsOfManyRequestsWereReturned()
    {
        // arrange
        const int requestsInFlight = 256;
        var services = new ServiceCollection();
        services
            .AddGraphQLGateway()
            .AddInMemoryConfiguration(
                ComposeSchemaDocument(
                    """
                    type Query {
                      field: String!
                    }
                    """));
        var executor = await services.BuildServiceProvider().GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var pool = executor.Schema.Services.GetRequiredService<ObjectPool<PooledRequestContext>>();
        var first = new PooledRequestContext[requestsInFlight];
        var second = new PooledRequestContext[requestsInFlight];

        for (var i = 0; i < first.Length; i++)
        {
            first[i] = pool.Get();
        }

        foreach (var context in first)
        {
            pool.Return(context);
        }

        // act
        for (var i = 0; i < second.Length; i++)
        {
            second[i] = pool.Get();
        }

        // assert
        Assert.Empty(second.Except(first));
    }
}
