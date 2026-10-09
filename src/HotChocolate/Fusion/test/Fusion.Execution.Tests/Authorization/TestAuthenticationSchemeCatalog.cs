using System.Collections.Immutable;

namespace HotChocolate.Fusion.Authorization;

internal sealed class TestAuthenticationSchemeCatalog : IAuthenticationSchemeCatalog
{
    private readonly ImmutableArray<string> _schemeNames;
    private readonly ImmutableDictionary<string, string> _challenges;

    public TestAuthenticationSchemeCatalog(params string[] schemeNames)
        : this(schemeNames, [])
    {
    }

    public TestAuthenticationSchemeCatalog(
        string[] schemeNames,
        ImmutableDictionary<string, string> challenges)
    {
        _schemeNames = [.. schemeNames];
        _challenges = challenges;
    }

    public ValueTask<ImmutableArray<string>> GetSchemeNamesAsync(CancellationToken cancellationToken)
        => new(_schemeNames);

    public ValueTask<string?> GetChallengeAsync(string schemeName, CancellationToken cancellationToken)
        => new(_challenges.GetValueOrDefault(schemeName));
}
