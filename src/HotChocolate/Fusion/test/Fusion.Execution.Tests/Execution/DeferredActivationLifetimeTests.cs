using System.Text;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization;
using HotChocolate.Fusion.Authorization.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution;

public class DeferredActivationLifetimeTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          foo: String
        }
        """;

    private const string Data = """{ "foo": "bar" }""";

    [Fact]
    public async Task DisposeAsync_Should_ReturnTheRentedActivationBits_When_DeferredStreamIsDroppedAfterInitialResult()
    {
        // arrange
        var executor = await CreateExecutorAsync(
            Schema,
            new AuthorizationTestClient(Data),
            new InMemoryPolicyRecorder());
        var contextPool = executor.Schema.Services.GetRequiredService<OperationPlanContextPool>();
        var context = contextPool.Rent();
        contextPool.Return(context);
        var request = CreateRequest(CreateDeferDocument(conditionCount: 70))
            .SetVariableValues(CreateVariableValues(conditionCount: 70, activeIndex: 65))
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await enumerator.MoveNextAsync();
        var activeWhileOpen = context.ActiveDeliveryGroups.RentedWords;
        var runningWhileOpen = context.RunningIncrementalPlans.RentedWords;
        await enumerator.DisposeAsync();
        await stream.DisposeAsync();

        // assert
        Assert.NotNull(activeWhileOpen);
        Assert.NotNull(runningWhileOpen);
        Assert.Null(context.ActiveDeliveryGroups.RentedWords);
        Assert.Null(context.RunningIncrementalPlans.RentedWords);
    }

    private static string CreateDeferDocument(int conditionCount)
    {
        var document = new StringBuilder("query(");

        for (var i = 0; i < conditionCount; i++)
        {
            document.Append($"$d{i}: Boolean! ");
        }

        document.Append(") { plain: foo");

        for (var i = 0; i < conditionCount; i++)
        {
            document.Append($" ... @defer(if: $d{i}) {{ f{i}: foo }}");
        }

        return document.Append(" }").ToString();
    }

    private static string CreateVariableValues(int conditionCount, int activeIndex)
    {
        var values = new List<string>(conditionCount);

        for (var i = 0; i < conditionCount; i++)
        {
            values.Add($"\"d{i}\":{(i == activeIndex ? "true" : "false")}");
        }

        return "{" + string.Join(",", values) + "}";
    }
}
