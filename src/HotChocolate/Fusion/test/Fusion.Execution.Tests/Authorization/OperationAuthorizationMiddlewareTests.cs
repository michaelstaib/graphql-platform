using System.Net;
using System.Security.Claims;
using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization.InMemory;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Authorization;

public class OperationAuthorizationMiddlewareTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          product: Product
          products: [Product]
          secret: String @authenticated
          guarded: String @policy(policies: [["finance"]])
          either: String @policy(policies: [["editor"], ["admin"]])
          both: String @policy(policies: [["editor", "admin"]])
          productById(id: ID!): Product @policy(policies: [["owner"]])
        }

        type Product {
          id: ID!
          name: String @authenticated
          price: Int @requiresScopes(scopes: [["read"]])
          cost: Int! @policy(policies: [["finance"]])
        }
        """;

    private const string MutationSchema =
        """
        type Query {
          field: String
        }

        type Mutation {
          rename: String @authenticated
        }
        """;

    private const string GhostSchema =
        """
        type Query {
          ghost: String @policy(policies: [["ghost"]])
        }
        """;

    private const string Data =
        """
        {
          "product": { "id": "1", "name": "Shoe", "price": 10, "cost": 5 },
          "products": [
            { "id": "1", "name": "Shoe", "price": 10, "cost": 5 },
            { "id": "2", "name": "Boot", "price": 20, "cost": 8 }
          ],
          "secret": "s3cret",
          "guarded": "g",
          "either": "e",
          "both": "b",
          "productById": { "id": "1", "name": "Shoe", "price": 10, "cost": 5 }
        }
        """;

    [Fact]
    public async Task InvokeAsync_Should_NotEvaluatePolicies_When_OperationIsUnprotected()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var recorder = new InMemoryPolicyRecorder();
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            recorder,
            policies => policies.Deny("finance"));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id } }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "product": {
                  "id": "1"
                }
              }
            }
            """);
        Assert.Empty(recorder.Records);
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnAllFields_When_EveryPolicyAllows()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var recorder = new InMemoryPolicyRecorder();
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            recorder,
            policies => policies.Allow("finance"));
        var user = Authenticated(new Claim("scope", "read"));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id name price cost } }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "product": {
                  "id": "1",
                  "name": "Shoe",
                  "price": 10,
                  "cost": 5
                }
              }
            }
            """);
        Assert.Equal(
            ["Product.cost"],
            recorder.Records.Select(r => r.Entry.Selection.Field.Coordinate.ToString()));
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnNullWithoutError_When_DenyHandlingIsNull()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            policies => policies.Allow("finance"));
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id name price } }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "product": {
                  "id": "1",
                  "name": "Shoe",
                  "price": null
                }
              }
            }
            """);
    }

    [Fact]
    public async Task InvokeAsync_Should_NeverReceiveDeniedData_When_PolicyDenies()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            policies => policies.Allow("finance"));
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id price } }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        Assert.Single(client.Requests).MatchInlineSnapshot(
            """
            query Op_915ec426_1($__fusion_auth_1: Boolean!) {
              product {
                id
                price @skip(if: $__fusion_auth_1)
              }
            }
            """);
    }

    [Fact]
    public async Task InvokeAsync_Should_NotCallScopeAndNamedPolicies_When_PrincipalIsMissing()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var recorder = new InMemoryPolicyRecorder();
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            recorder,
            policies => policies.Allow("finance"));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id name price cost } }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "product",
                    "cost"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "product": null
              }
            }
            """);
        Assert.Empty(recorder.Records);
    }

    [Fact]
    public async Task InvokeAsync_Should_NotFetch_When_EveryRootSelectionOfTheNodeIsDenied()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder());

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "secret": null
              }
            }
            """);
        Assert.Empty(client.Requests);
    }

    [Theory]
    [InlineData(true, true, "e", "b")]
    [InlineData(true, false, "e", null)]
    [InlineData(false, true, "e", null)]
    [InlineData(false, false, null, null)]
    public async Task InvokeAsync_Should_CombinePolicyGroups_When_PoliciesAreAlternativesAndConjunctions(
        bool editorAllows,
        bool adminAllows,
        string? expectedEither,
        string? expectedBoth)
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            policies => policies
                .Evaluate("editor", (_, _) => editorAllows)
                .Evaluate("admin", (_, _) => adminAllows));
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ either both }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        using var document = JsonDocument.Parse(result.ToJson());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(expectedEither, data.GetProperty("either").GetString());
        Assert.Equal(expectedBoth, data.GetProperty("both").GetString());
    }

    [Fact]
    public async Task InvokeAsync_Should_NotExecuteMutation_When_RootFieldIsDenied()
    {
        // arrange
        var client = new AuthorizationTestClient("""{"rename":"done"}""");
        var executor = await CreateExecutorAsync(
            MutationSchema,
            client,
            new InMemoryPolicyRecorder());

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("mutation { rename }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "rename": null
              }
            }
            """);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnGenericError_When_PolicyIsNotResolved()
    {
        // arrange
        var client = new AuthorizationTestClient("""{"ghost":"gh"}""");
        var executor = await CreateExecutorAsync(
            GhostSchema,
            client,
            new InMemoryPolicyRecorder(),
            configure: builder => builder.ModifyAuthorizationOptions(
                options => options.DisableAuthorizationValidation = true));
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ ghost }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The authorization of the request could not be evaluated."
                }
              ]
            }
            """);
        Assert.Equal(
            HttpStatusCode.InternalServerError,
            result.ExpectOperationResult().ContextData[ExecutionContextData.HttpStatusCode]);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task InvokeAsync_Should_SkipEvaluation_When_RequestIsWarmup()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var recorder = new InMemoryPolicyRecorder();
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            recorder,
            policies => policies.Deny("finance"));
        var request = CreateRequest("{ product { id cost } }").MarkAsWarmupRequest().Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<WarmupExecutionResult>(result);
        Assert.Empty(recorder.Records);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task InvokeAsync_Should_EvaluateEachVariableSetSeparately_When_RequestIsBatched()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            policies => policies.Evaluate(
                "owner",
                (_, entry) => entry.Arguments["id"] is StringValueNode { Value: "1" }));
        var request = CreateRequest("query($id: ID!) { productById(id: $id) { id } }", Authenticated())
            .SetVariableValues("""[{"id":"1"},{"id":"2"}]""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResultBatch().Results.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "productById": {
                      "id": "1"
                    }
                  }
                }
                """,
                """
                {
                  "data": {
                    "productById": null
                  }
                }
                """
            ]);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task InvokeAsync_Should_OnlyEvaluateIncludedSelections_When_SelectionIsSkippedByTheClient(
        bool skip,
        int expectedRecords)
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var recorder = new InMemoryPolicyRecorder();
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            recorder,
            policies => policies.Allow("finance"));
        var request = CreateRequest("query($skip: Boolean!) { guarded @skip(if: $skip) product { id } }", Authenticated())
            .SetVariableValues($$"""{"skip":{{(skip ? "true" : "false")}}}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(expectedRecords, recorder.Records.Count);
    }
}
