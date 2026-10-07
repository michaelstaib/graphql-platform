using System.Collections.Immutable;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Authorization;

public class AuthorizationStartupValidationTests : FusionTestBase
{
    private const string Directives =
        """
        directive @authenticated on FIELD_DEFINITION | OBJECT | INTERFACE | ENUM | SCALAR

        directive @requiresScopes(scopes: [[String!]!]!)
            on FIELD_DEFINITION | OBJECT | INTERFACE | ENUM | SCALAR

        directive @policy(policies: [[String!]!]!)
            on FIELD_DEFINITION | OBJECT | INTERFACE | ENUM | SCALAR
        """;

    private const string NoSchemeMessage =
        "The schema uses authorization directives but no authentication scheme is registered. "
        + "Register an authentication scheme, or disable the authorization validation.";

    [Theory]
    [InlineData("secret: String @authenticated")]
    [InlineData("""secret: String @requiresScopes(scopes: [["read"]])""")]
    [InlineData("""secret: String @policy(policies: [["admin"]])""")]
    public async Task Startup_Should_Fail_When_SchemaUsesAuthorizationAndNoSchemeIsRegistered(string field)
    {
        // arrange
        var provider = CreateProvider(
            field,
            catalog: new TestAuthenticationSchemeCatalog(),
            policies: policies => policies.Allow("admin"));

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(NoSchemeMessage, exception.Message);
    }

    [Fact]
    public async Task Startup_Should_Succeed_When_SchemaUsesAuthorizationAndSchemeIsRegistered()
    {
        // arrange
        var provider = CreateProvider(
            "secret: String @authenticated",
            catalog: new TestAuthenticationSchemeCatalog("Bearer"));

        // act
        var executor = await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, executor.Schema.Name);
    }

    [Fact]
    public async Task Startup_Should_Fail_When_ListedSchemeIsNotRegistered()
    {
        // arrange
        var provider = CreateProvider(
            "secret: String @authenticated",
            catalog: new TestAuthenticationSchemeCatalog("Bearer"),
            configure: o => o.Schemes = ImmutableArray.Create("Bearer", "Cookie"));

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(
            "The authentication scheme 'Cookie' of the authorization options is not registered. "
            + "Register it, remove it from the schemes, or disable the authorization validation.",
            exception.Message);
    }

    [Fact]
    public async Task Startup_Should_Fail_When_ListedSchemeIsNotRegisteredAndSchemaUsesNoAuthorization()
    {
        // arrange
        var provider = CreateProvider(
            "field: String",
            catalog: new TestAuthenticationSchemeCatalog(),
            configure: o => o.Schemes = ImmutableArray.Create("Bearer"));

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(
            "The authentication scheme 'Bearer' of the authorization options is not registered. "
            + "Register it, remove it from the schemes, or disable the authorization validation.",
            exception.Message);
    }

    [Theory]
    [InlineData("secret: String @authenticated")]
    [InlineData("field: String")]
    public async Task Startup_Should_Fail_When_SchemesAreEmpty(string field)
    {
        // arrange
        var provider = CreateProvider(
            field,
            catalog: new TestAuthenticationSchemeCatalog("Bearer"),
            configure: o => o.Schemes = ImmutableArray<string>.Empty);

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(
            "The Schemes list is empty; omit it to use every registered scheme or list at least one.",
            exception.Message);
    }

    [Theory]
    [InlineData("secret: String @authenticated")]
    [InlineData("field: String")]
    public async Task Startup_Should_Fail_When_SchemesAreListedAndHostHasNoAuthenticationSchemeCatalog(string field)
    {
        // arrange
        var provider = CreateProvider(
            field,
            catalog: null,
            configure: o => o.Schemes = ImmutableArray.Create("Bearer"));

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(
            "The Schemes option lists authentication schemes but the host exposes no authentication schemes. "
            + "Register the catalog through HotChocolate.Fusion.AspNetCore, or do not set Schemes.",
            exception.Message);
    }

    [Fact]
    public async Task Startup_Should_Succeed_When_SchemaUsesNoAuthorizationAndListedSchemesAreRegistered()
    {
        // arrange
        var provider = CreateProvider(
            "field: String",
            catalog: new TestAuthenticationSchemeCatalog("Bearer", "Cookie"),
            configure: o => o.Schemes = ImmutableArray.Create("Cookie"));

        // act
        var executor = await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, executor.Schema.Name);
    }

    [Fact]
    public async Task Startup_Should_Succeed_When_ListedSchemesAreRegistered()
    {
        // arrange
        var provider = CreateProvider(
            "secret: String @authenticated",
            catalog: new TestAuthenticationSchemeCatalog("Bearer", "Cookie"),
            configure: o => o.Schemes = ImmutableArray.Create("Cookie"));

        // act
        var executor = await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, executor.Schema.Name);
    }

    [Fact]
    public async Task Startup_Should_Succeed_When_SchemaUsesNoAuthorizationAndNoSchemeIsRegistered()
    {
        // arrange
        var provider = CreateProvider(
            "field: String",
            catalog: new TestAuthenticationSchemeCatalog());

        // act
        var executor = await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, executor.Schema.Name);
    }

    [Fact]
    public async Task Startup_Should_Fail_When_SchemaUsesAuthorizationAndHostHasNoAuthenticationSchemeCatalog()
    {
        // arrange
        var provider = CreateProvider("secret: String @authenticated", catalog: null);

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(
            "The schema uses authorization directives but the host exposes no authentication schemes. "
            + "Add the gateway through HotChocolate.Fusion.AspNetCore, or set DisableAuthorizationValidation.",
            exception.Message);
    }

    [Fact]
    public async Task Startup_Should_Fail_When_PolicyNameIsNotResolvable()
    {
        // arrange
        var provider = CreateProvider(
            """secret: String @policy(policies: [["admin", "auditor"]])""",
            catalog: new TestAuthenticationSchemeCatalog("Bearer"),
            policies: policies => policies.Allow("admin"));

        // act
        var act = async () => await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(
            "No policy provider knows the policy 'auditor' of the directive '@policy'.",
            exception.Message);
    }

    [Fact]
    public async Task Startup_Should_Succeed_When_EveryPolicyNameIsResolvable()
    {
        // arrange
        var provider = CreateProvider(
            """secret: String @policy(policies: [["admin", "auditor"]])""",
            catalog: new TestAuthenticationSchemeCatalog("Bearer"),
            policies: policies => policies.Allow("admin").Deny("auditor"));

        // act
        var executor = await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, executor.Schema.Name);
    }

    [Fact]
    public async Task Startup_Should_Succeed_When_ValidationIsDisabled()
    {
        // arrange
        var provider = CreateProvider(
            """
            secret: String @authenticated
            guarded: String @policy(policies: [["admin"]])
            """,
            catalog: new TestAuthenticationSchemeCatalog(),
            configure: o =>
            {
                o.Schemes = ImmutableArray.Create("Bearer");
                o.DisableAuthorizationValidation = true;
            });

        // act
        var executor = await provider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, executor.Schema.Name);
    }

    private static IServiceProvider CreateProvider(
        string fields,
        IAuthenticationSchemeCatalog? catalog,
        Action<FusionAuthorizationOptions>? configure = null,
        Action<InMemory.InMemoryPolicyBuilder>? policies = null)
    {
        var services = new ServiceCollection();
        var builder = services
            .AddGraphQLGateway()
            .AddInMemoryConfiguration(
                ComposeSchemaDocument(
                    $$"""
                    type Query {
                      {{fields}}
                    }

                    {{Directives}}
                    """));

        if (catalog is not null)
        {
            builder.ConfigureSchemaServices((_, sc) => sc.AddSingleton(catalog));
        }

        if (configure is not null)
        {
            builder.ModifyAuthorizationOptions(configure);
        }

        if (policies is not null)
        {
            builder.AddInMemoryPolicies(policies);
        }

        return services.BuildServiceProvider();
    }
}
