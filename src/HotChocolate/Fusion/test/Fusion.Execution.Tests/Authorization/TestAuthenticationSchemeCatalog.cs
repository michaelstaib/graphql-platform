using System.Collections.Immutable;

namespace HotChocolate.Fusion.Authorization;

internal sealed class TestAuthenticationSchemeCatalog(params string[] schemeNames) : IAuthenticationSchemeCatalog
{
    public ValueTask<ImmutableArray<string>> GetSchemeNamesAsync(CancellationToken cancellationToken)
        => new([.. schemeNames]);
}
