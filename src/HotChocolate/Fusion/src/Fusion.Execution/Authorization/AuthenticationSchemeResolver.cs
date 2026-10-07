using System.Collections.Immutable;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Resolves the authentication schemes the gateway authenticates against from the
/// <see cref="FusionAuthorizationOptions"/> and the schemes the host has registered.
/// </summary>
internal sealed class AuthenticationSchemeResolver
{
    private readonly FusionAuthorizationOptions _options;
    private readonly IAuthenticationSchemeCatalog? _catalog;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthenticationSchemeResolver"/>.
    /// </summary>
    /// <param name="options">
    /// The authorization options.
    /// </param>
    /// <param name="catalog">
    /// The registered schemes, or <c>null</c> if the host has no authentication schemes.
    /// </param>
    public AuthenticationSchemeResolver(
        FusionAuthorizationOptions options,
        IAuthenticationSchemeCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _catalog = catalog;
    }

    /// <summary>
    /// Gets a value indicating whether the host exposes its registered authentication schemes.
    /// </summary>
    public bool HasCatalog => _catalog is not null;

    /// <summary>
    /// Gets the names of the registered authentication schemes.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that signals that the operation was aborted.
    /// </param>
    public ValueTask<ImmutableArray<string>> GetRegisteredAsync(CancellationToken cancellationToken)
        => _catalog is null
            ? new ValueTask<ImmutableArray<string>>([])
            : _catalog.GetSchemeNamesAsync(cancellationToken);

    /// <summary>
    /// Gets the value of the <c>WWW-Authenticate</c> header that advertises the selected schemes
    /// as bare names, or <c>null</c> if no scheme is registered.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that signals that the operation was aborted.
    /// </param>
    public async ValueTask<string?> GetChallengeAsync(CancellationToken cancellationToken)
    {
        var registered = await GetRegisteredAsync(cancellationToken).ConfigureAwait(false);
        var selected = _options.Schemes ?? registered;
        var challenged = selected.Where(registered.Contains).ToImmutableArray();

        return challenged.IsEmpty ? null : string.Join(", ", challenged);
    }
}
