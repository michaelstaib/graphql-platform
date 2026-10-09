using System.Text;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization;
using HotChocolate.Fusion.Authorization.InMemory;
using HotChocolate.Fusion.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        var activationPool = new CountingArrayPool();
        var executor = await CreateExecutorAsync(
            Schema,
            new AuthorizationTestClient(Data),
            new InMemoryPolicyRecorder(),
            configure: builder => builder.ConfigureSchemaServices(
                (_, services) =>
                {
                    services.RemoveAll<OperationPlanContextPool>();
                    services.AddSingleton(sp => new OperationPlanContextPool(
                        sp.GetRequiredService<INodeIdParser>(),
                        sp.GetRequiredService<IFusionExecutionDiagnosticEvents>(),
                        sp.GetRequiredService<IErrorHandler>(),
                        activationPool,
                        levels: [64],
                        trimInterval: TimeSpan.FromMinutes(2)));
                }));
        var request = CreateRequest(CreateDeferDocument(conditionCount: 70))
            .SetVariableValues(CreateVariableValues(conditionCount: 70, activeIndex: 65))
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);
        var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await enumerator.MoveNextAsync();
        var rentedWhileOpen = activationPool.Rented;
        var outstandingWhileOpen = activationPool.Outstanding;
        await enumerator.DisposeAsync();
        await stream.DisposeAsync();

        // assert
        Assert.Equal(2, outstandingWhileOpen);
        Assert.Collection(
            activationPool.Returned,
            active => Assert.Same(rentedWhileOpen[0], active),
            running => Assert.Same(rentedWhileOpen[1], running));
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
