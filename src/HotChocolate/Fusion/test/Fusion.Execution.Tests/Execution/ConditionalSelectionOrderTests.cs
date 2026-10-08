using HotChocolate.Fusion.Authorization;
using HotChocolate.Fusion.Authorization.InMemory;

namespace HotChocolate.Fusion.Execution;

public class ConditionalSelectionOrderTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          first: String
          second: String
          third: String
          product: Product
        }

        type Product {
          id: ID!
          name: String
          price: Int
        }
        """;

    private const string Data =
        """
        {
          "first": "a",
          "second": "b",
          "third": "c",
          "product": { "id": "1", "name": "Shoe", "price": 10 }
        }
        """;

    [Theory]
    [InlineData("query($v: Boolean!) { first @skip(if: $v) product { id } }", false, """
        {
          "data": {
            "first": "a",
            "product": {
              "id": "1"
            }
          }
        }
        """)]
    [InlineData("query($v: Boolean!) { first @include(if: $v) second product { id } }", true, """
        {
          "data": {
            "first": "a",
            "second": "b",
            "product": {
              "id": "1"
            }
          }
        }
        """)]
    [InlineData("query($v: Boolean!) { product { id } first @skip(if: $v) second }", false, """
        {
          "data": {
            "product": {
              "id": "1"
            },
            "first": "a",
            "second": "b"
          }
        }
        """)]
    [InlineData("query($v: Boolean!) { first @skip(if: $v) second third @skip(if: $v) }", false, """
        {
          "data": {
            "first": "a",
            "second": "b",
            "third": "c"
          }
        }
        """)]
    [InlineData("query($v: Boolean!) { ... @skip(if: $v) { first } second ... @skip(if: $v) { third } }", false, """
        {
          "data": {
            "first": "a",
            "second": "b",
            "third": "c"
          }
        }
        """)]
    [InlineData("query($v: Boolean!) { first @skip(if: $v) second @include(if: $v) third }", false, """
        {
          "data": {
            "first": "a",
            "third": "c"
          }
        }
        """)]
    [InlineData("query($v: Boolean!) { product { name @skip(if: $v) id price @skip(if: $v) } }", false, """
        {
          "data": {
            "product": {
              "name": "Shoe",
              "id": "1",
              "price": 10
            }
          }
        }
        """)]
    public async Task ExecuteAsync_Should_FollowRequestOrder_When_SelectionIsConditional(
        string document,
        bool variable,
        string expected)
    {
        // arrange
        var executor = await CreateExecutorAsync(
            Schema,
            new AuthorizationTestClient(Data),
            new InMemoryPolicyRecorder());
        var request = CreateRequest(document)
            .SetVariableValues($$"""{"v":{{(variable ? "true" : "false")}}}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(expected);
    }
}
