using System.Collections.Immutable;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Provides the names of the authentication schemes the host has registered.
/// </summary>
internal interface IAuthenticationSchemeCatalog
{
    /// <summary>
    /// Gets the names of all registered authentication schemes.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that signals that the operation was aborted.
    /// </param>
    ValueTask<ImmutableArray<string>> GetSchemeNamesAsync(CancellationToken cancellationToken);
}
