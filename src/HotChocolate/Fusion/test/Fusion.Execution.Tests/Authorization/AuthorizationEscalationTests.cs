using System.Net;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization.InMemory;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Authorization;

public class AuthorizationEscalationTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          product: Product
          productById(id: ID!): Product @policy(policies: [["owner"]])
        }

        type Product {
          id: ID!
          name: String @authenticated
          price: Int @requiresScopes(scopes: [["read"]])
        }
        """;

    private const string Data =
        """
        {
          "product": { "id": "1", "name": "Shoe", "price": 10 },
          "productById": { "id": "1", "name": "Shoe", "price": 10 }
        }
        """;

    [Fact]
    public async Task Execute_Should_RejectWithChallenge_When_UnauthenticatedDenialMeetsTheLadder()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateEscalatingExecutorAsync(client, RejectRequestOn.OnUnauthenticated);

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id name } }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The current user is not authenticated.",
                  "extensions": {
                    "code": "AUTH_NOT_AUTHENTICATED"
                  }
                }
              ]
            }
            """);
        var contextData = result.ExpectOperationResult().ContextData;
        Assert.Equal(HttpStatusCode.Unauthorized, contextData[ExecutionContextData.HttpStatusCode]);
        Assert.Equal("Bearer", contextData[ExecutionContextData.WwwAuthenticateHeaderValue]);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Execute_Should_ReturnPartialData_When_UnauthorizedDenialIsBelowTheLadder()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateEscalatingExecutorAsync(client, RejectRequestOn.OnUnauthenticated);
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id price } }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "product": {
                  "id": "1",
                  "price": null
                }
              }
            }
            """);
        Assert.False(result.ExpectOperationResult().ContextData.ContainsKey(ExecutionContextData.HttpStatusCode));
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Execute_Should_RejectWithoutChallenge_When_UnauthorizedDenialMeetsTheLadder()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateEscalatingExecutorAsync(client, RejectRequestOn.OnUnauthorized);
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id price } }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The current user is not authorized to access this resource.",
                  "extensions": {
                    "code": "AUTH_NOT_AUTHORIZED"
                  }
                }
              ]
            }
            """);
        var contextData = result.ExpectOperationResult().ContextData;
        Assert.Equal(HttpStatusCode.Forbidden, contextData[ExecutionContextData.HttpStatusCode]);
        Assert.False(contextData.ContainsKey(ExecutionContextData.WwwAuthenticateHeaderValue));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Execute_Should_RejectUnauthenticatedWithoutChallenge_When_NoSchemeIsAvailable()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateEscalatingExecutorAsync(
            client,
            RejectRequestOn.OnUnauthorized,
            configure: builder =>
            {
                builder.ModifyAuthorizationOptions(options => options.DisableAuthorizationValidation = true);
                builder.ConfigureSchemaServices(
                    (_, sc) => sc.AddSingleton<IAuthenticationSchemeCatalog>(
                        new TestAuthenticationSchemeCatalog()));
            });

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id name } }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        var contextData = result.ExpectOperationResult().ContextData;
        Assert.Equal(HttpStatusCode.Unauthorized, contextData[ExecutionContextData.HttpStatusCode]);
        Assert.False(contextData.ContainsKey(ExecutionContextData.WwwAuthenticateHeaderValue));
    }

    [Fact]
    public async Task Execute_Should_NameCoordinateDirectiveAndScopes_When_RejectionIsAttributed()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateEscalatingExecutorAsync(
            client,
            RejectRequestOn.OnUnauthorized,
            configure: builder => builder.ModifyAuthorizationOptions(
                options => options.EnableAttribution = true));
        var user = Authenticated();

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ product { id price } }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The current user is not authorized to access this resource.",
                  "extensions": {
                    "code": "AUTH_NOT_AUTHORIZED",
                    "coordinate": "Product.price",
                    "directive": "requiresScopes",
                    "requiredScopes": [
                      [
                        "read"
                      ]
                    ]
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_RejectTheWholeBatch_When_OneVariableSetIsDenied()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateEscalatingExecutorAsync(
            client,
            RejectRequestOn.OnUnauthorized,
            policies => policies.Evaluate(
                "owner",
                (_, entry) => entry.Arguments["id"] is StringValueNode { Value: "1" }));
        var request = CreateRequest(
                "query($id: ID!) { productById(id: $id) { id } }",
                Authenticated())
            .SetVariableValues("""[{"id":"1"},{"id":"2"}]""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The current user is not authorized to access this resource.",
                  "extensions": {
                    "code": "AUTH_NOT_AUTHORIZED"
                  }
                }
              ]
            }
            """);
        Assert.Empty(client.Requests);
    }

    private static Task<IRequestExecutor> CreateEscalatingExecutorAsync(
        AuthorizationTestClient client,
        RejectRequestOn rejectRequestOn,
        Action<InMemoryPolicyBuilder>? policies = null,
        Action<IFusionGatewayBuilder>? configure = null)
        => CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            policies,
            builder =>
            {
                builder.ModifyAuthorizationOptions(options => options.RejectRequestOn = rejectRequestOn);
                configure?.Invoke(builder);
            });
}
