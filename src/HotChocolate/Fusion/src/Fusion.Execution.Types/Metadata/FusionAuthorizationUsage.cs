namespace HotChocolate.Fusion.Types.Metadata;

/// <summary>
/// Holds which authorization requirements are used across the composed schema and the distinct
/// policy names they reference. Stored as a schema feature.
/// </summary>
internal sealed class FusionAuthorizationUsage
{
    private readonly SortedSet<string> _policyNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a value indicating whether any member requires an authenticated user.
    /// </summary>
    public bool UsesAuthenticated { get; private set; }

    /// <summary>
    /// Gets a value indicating whether any member requires scopes.
    /// </summary>
    public bool UsesScopes { get; private set; }

    /// <summary>
    /// Gets a value indicating whether any member requires policies.
    /// </summary>
    public bool UsesPolicies => _policyNames.Count > 0;

    /// <summary>
    /// Gets a value indicating whether any member carries an authorization requirement.
    /// </summary>
    public bool IsUsed => UsesAuthenticated || UsesScopes || UsesPolicies;

    /// <summary>
    /// Gets the distinct policy names in ordinal order.
    /// </summary>
    public IReadOnlyCollection<string> PolicyNames => _policyNames;

    internal void Add(Directives.AuthorizationDirective authorization)
    {
        UsesAuthenticated |= authorization.Authenticated;
        UsesScopes |= authorization.Scopes.Length > 0;

        foreach (var group in authorization.Policies)
        {
            foreach (var name in group)
            {
                _policyNames.Add(name);
            }
        }
    }
}
