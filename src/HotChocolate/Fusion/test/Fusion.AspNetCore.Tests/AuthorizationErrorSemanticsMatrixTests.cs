using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HotChocolate.Fusion.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public class AuthorizationErrorSemanticsMatrixTests : FusionTestBase
{
    private const string UserHeader = "X-Test-User";
    private const string Member = "member";
    private const string Reader = "reader";

    private const string MatrixSchema =
        """
        directive @authenticated on FIELD_DEFINITION | OBJECT | INTERFACE | ENUM | SCALAR

        directive @requiresScopes(scopes: [[String!]!]!)
            on FIELD_DEFINITION | OBJECT | INTERFACE | ENUM | SCALAR

        directive @policy(policies: [[String!]!]!)
            on FIELD_DEFINITION | OBJECT | INTERFACE | ENUM | SCALAR

        type Query {
          open: String
          me: String @authenticated
          admin: String @requiresScopes(scopes: [["admin"]])
          guarded: String @policy(policies: [["finance"]])
          product: Product
          strictProduct: Product!
          products: [Product]
          strictProducts: [Product!]
        }

        type Product {
          id: ID!
          name: String @authenticated
          price: Int @requiresScopes(scopes: [["read"]])
          cost: Int! @policy(policies: [["finance"]])
        }
        """;

    private static readonly (string Name, string Query, string? User)[] s_fieldCells =
    [
        ("Unauthenticated nullable field", "{ me }", null),
        ("Unauthorized nullable field", "{ admin }", Member),
        ("Anonymous principal on a scope-protected field", "{ admin }", null),
        ("Denied policy on nullable field", "{ guarded }", Reader),
        ("Allowed selections", "{ product { id name price } }", Reader),
        ("Partial data beside a denied field", "{ open me }", null),
        ("Denied non-null field under a nullable parent", "{ product { id cost } }", Reader),
        ("Denied non-null field under a non-null parent", "{ strictProduct { id cost } }", Reader),
        ("Denied nullable field in every list element", "{ products { id price } }", Member),
        ("Denied non-null field in nullable list elements", "{ products { id cost } }", Reader),
        ("Denied non-null field in non-null list elements", "{ strictProducts { id cost } }", Reader)
    ];

    private static readonly (string Name, string Query, string? User)[] s_escalationCells =
    [
        ("Unauthenticated denial", "{ open me }", null),
        ("Unauthorized denial", "{ open admin }", Member),
        ("Denied non-null field", "{ product { id cost } }", Reader),
        ("Nothing denied", "{ open product { id price } }", Reader),
        ("Unprotected operation", "{ open }", null)
    ];

    [Fact]
    public async Task Response_Should_RenderSilentNulls_When_DenyHandlingIsNull()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddScenarioAsync(
            snapshot,
            "Null",
            o => o.DenyHandling = DenyHandling.Null,
            s_fieldCells);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Response_Should_RenderIndexedFieldErrors_When_DenyHandlingIsError()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddScenarioAsync(
            snapshot,
            "Error",
            o => o.DenyHandling = DenyHandling.Error,
            s_fieldCells);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Response_Should_RenderAttribution_When_AttributionIsOn()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddScenarioAsync(
            snapshot,
            "Error with attribution",
            o =>
            {
                o.DenyHandling = DenyHandling.Error;
                o.EnableAttribution = true;
            },
            s_fieldCells);
        await AddScenarioAsync(
            snapshot,
            "Null with attribution",
            o =>
            {
                o.DenyHandling = DenyHandling.Null;
                o.EnableAttribution = true;
            },
            s_fieldCells);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Response_Should_RejectRequestOnlyForUnauthenticated_When_RejectRequestOnIsOnUnauthenticated()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddScenarioAsync(
            snapshot,
            "OnUnauthenticated",
            o =>
            {
                o.DenyHandling = DenyHandling.Error;
                o.RejectRequestOn = RejectRequestOn.OnUnauthenticated;
            },
            s_escalationCells);
        await AddScenarioAsync(
            snapshot,
            "OnUnauthenticated with attribution",
            o =>
            {
                o.DenyHandling = DenyHandling.Error;
                o.RejectRequestOn = RejectRequestOn.OnUnauthenticated;
                o.EnableAttribution = true;
            },
            s_escalationCells);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Response_Should_RejectRequestForEveryDenial_When_RejectRequestOnIsOnUnauthorized()
    {
        // arrange
        var snapshot = Snapshot.Create();

        // act
        await AddScenarioAsync(
            snapshot,
            "OnUnauthorized",
            o =>
            {
                o.DenyHandling = DenyHandling.Error;
                o.RejectRequestOn = RejectRequestOn.OnUnauthorized;
            },
            s_escalationCells);
        await AddScenarioAsync(
            snapshot,
            "OnUnauthorized with attribution",
            o =>
            {
                o.DenyHandling = DenyHandling.Error;
                o.RejectRequestOn = RejectRequestOn.OnUnauthorized;
                o.EnableAttribution = true;
            },
            s_escalationCells);
        await AddScenarioAsync(
            snapshot,
            "OnUnauthorized with null deny handling",
            o =>
            {
                o.DenyHandling = DenyHandling.Null;
                o.RejectRequestOn = RejectRequestOn.OnUnauthorized;
            },
            s_escalationCells);

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    private async Task AddScenarioAsync(
        Snapshot snapshot,
        string scenario,
        Action<FusionAuthorizationOptions> configure,
        (string Name, string Query, string? User)[] cells)
    {
        using var server = CreateSourceSchema("A", MatrixSchema);
        using var gateway = await CreateCompositeSchemaAsync(
            [("A", server)],
            configureServices: AddCookieSchemes,
            configureApplication: UseTestUser,
            configureGatewayBuilder: b => b
                .ModifyAuthorizationOptions(configure)
                .AddInMemoryPolicies(p => p.Deny("finance")),
            includeOperationPlan: false);
        using var client = gateway.CreateClient();

        foreach (var (name, query, user) in cells)
        {
            using var response = await PostAsync(client, query, user);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var challenge = response.Headers.TryGetValues("WWW-Authenticate", out var values)
                ? $", WWW-Authenticate: {string.Join(", ", values)}"
                : string.Empty;

            snapshot.Add(
                Format(body),
                $"{scenario}: {name} ({user ?? "anonymous"}) -> {(int)response.StatusCode} {response.StatusCode}{challenge}",
                MarkdownLanguages.Json);
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string query, string? user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/graphql")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { query }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/graphql-response+json"));

        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Format(string body)
    {
        using var document = JsonDocument.Parse(body);
        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void UseTestUser(IApplicationBuilder app)
    {
        app.UseRouting();
        app.Use(
            async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue(UserHeader, out var user))
                {
                    var claims = user == Reader ? [new Claim("scope", "read")] : Array.Empty<Claim>();
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
                }

                await next(context);
            });
        app.UseEndpoints(endpoint => endpoint.MapGraphQL());
    }

    private static void AddCookieSchemes(IServiceCollection services)
        => services
            .AddAuthentication()
            .AddCookie("Cookies")
            .AddCookie("Session");
}
