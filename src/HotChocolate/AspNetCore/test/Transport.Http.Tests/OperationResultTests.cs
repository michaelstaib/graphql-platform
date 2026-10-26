using System.Text;
using System.Text.Json;

namespace HotChocolate.Transport.Http.Tests;

public class OperationResultTests
{
    [Fact]
    public void Parse_Should_ExposeIncrementalAndHasNext_When_PayloadIsIncremental()
    {
        // arrange
        var payload = Encoding.UTF8.GetBytes(
            """{"incremental":[{"data":{"name":"Abc"},"path":["product"]}],"hasNext":false}""");

        // act
        using var result = OperationResult.Parse(payload);

        // assert
        Assert.Equal(JsonValueKind.Array, result.Incremental.ValueKind);
        Assert.Equal(
            """[{"data":{"name":"Abc"},"path":["product"]}]""",
            result.Incremental.GetRawText());
        Assert.False(result.HasNext);
    }

    [Fact]
    public void Parse_Should_LeaveIncrementalAndHasNextUnset_When_PayloadIsNotIncremental()
    {
        // arrange
        var payload = Encoding.UTF8.GetBytes("""{"data":{"name":"Abc"}}""");

        // act
        using var result = OperationResult.Parse(payload);

        // assert
        Assert.Equal(JsonValueKind.Undefined, result.Incremental.ValueKind);
        Assert.Null(result.HasNext);
    }
}
