using System.Collections.Immutable;
using HotChocolate.Fusion.Authorization;
using Microsoft.AspNetCore.Authentication;

namespace HotChocolate.Fusion.AspNetCore;

internal sealed class AuthenticationSchemeCatalog(IAuthenticationSchemeProvider? schemeProvider)
    : IAuthenticationSchemeCatalog
{
    public async ValueTask<ImmutableArray<string>> GetSchemeNamesAsync(CancellationToken cancellationToken)
    {
        if (schemeProvider is null)
        {
            return [];
        }

        var schemes = await schemeProvider.GetAllSchemesAsync().ConfigureAwait(false);

        return [.. schemes.Select(scheme => scheme.Name)];
    }
}
