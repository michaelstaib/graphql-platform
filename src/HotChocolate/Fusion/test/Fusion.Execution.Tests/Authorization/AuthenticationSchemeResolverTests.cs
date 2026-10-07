using System.Collections.Immutable;

namespace HotChocolate.Fusion.Authorization;

public class AuthenticationSchemeResolverTests
{
    [Fact]
    public async Task GetChallengeAsync_Should_AdvertiseEveryRegisteredScheme_When_SchemesAreUnset()
    {
        // arrange
        var schemeResolver = new AuthenticationSchemeResolver(
            new FusionAuthorizationOptions(),
            new TestAuthenticationSchemeCatalog("Bearer", "Cookie"));

        // act
        var challenge = await schemeResolver.GetChallengeAsync(CancellationToken.None);

        // assert
        Assert.Equal("Bearer, Cookie", challenge);
    }

    [Fact]
    public async Task GetChallengeAsync_Should_AdvertiseOnlyListedSchemes_When_SchemesAreSet()
    {
        // arrange
        var schemeResolver = new AuthenticationSchemeResolver(
            new FusionAuthorizationOptions { Schemes = ImmutableArray.Create("Cookie") },
            new TestAuthenticationSchemeCatalog("Bearer", "Cookie"));

        // act
        var challenge = await schemeResolver.GetChallengeAsync(CancellationToken.None);

        // assert
        Assert.Equal("Cookie", challenge);
    }

    [Fact]
    public async Task GetChallengeAsync_Should_SkipUnregisteredSchemes_When_ListedSchemeIsNotRegistered()
    {
        // arrange
        var schemeResolver = new AuthenticationSchemeResolver(
            new FusionAuthorizationOptions { Schemes = ImmutableArray.Create("Negotiate", "Bearer") },
            new TestAuthenticationSchemeCatalog("Bearer"));

        // act
        var challenge = await schemeResolver.GetChallengeAsync(CancellationToken.None);

        // assert
        Assert.Equal("Bearer", challenge);
    }

    [Fact]
    public async Task GetChallengeAsync_Should_ReturnNull_When_NoSchemeIsRegistered()
    {
        // arrange
        var schemeResolver = new AuthenticationSchemeResolver(
            new FusionAuthorizationOptions { Schemes = ImmutableArray.Create("Bearer") },
            new TestAuthenticationSchemeCatalog());

        // act
        var challenge = await schemeResolver.GetChallengeAsync(CancellationToken.None);

        // assert
        Assert.Null(challenge);
    }

    [Fact]
    public async Task GetChallengeAsync_Should_ReturnNull_When_HostHasNoCatalog()
    {
        // arrange
        var schemeResolver = new AuthenticationSchemeResolver(new FusionAuthorizationOptions(), catalog: null);

        // act
        var challenge = await schemeResolver.GetChallengeAsync(CancellationToken.None);

        // assert
        Assert.Null(challenge);
    }
}
