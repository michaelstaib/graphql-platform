using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Types.Metadata;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Fails the startup of a gateway whose authorization requirements cannot be enforced.
/// </summary>
internal static class AuthorizationStartupValidator
{
    /// <summary>
    /// Validates that a schema that uses authorization has a usable authentication scheme, that
    /// every listed scheme is registered and that every policy name resolves.
    /// </summary>
    /// <param name="usage">
    /// The authorization requirements the schema uses.
    /// </param>
    /// <param name="options">
    /// The authorization options.
    /// </param>
    /// <param name="schemeResolver">
    /// The authentication schemes of the host.
    /// </param>
    /// <param name="policyResolver">
    /// The resolver that is asked for every policy name of the schema.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that signals that the operation was aborted.
    /// </param>
    public static async ValueTask ValidateAsync(
        FusionAuthorizationUsage? usage,
        FusionAuthorizationOptions options,
        AuthenticationSchemeResolver schemeResolver,
        IPolicyResolver policyResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemeResolver);
        ArgumentNullException.ThrowIfNull(policyResolver);

        if (options.DisableAuthorizationValidation)
        {
            return;
        }

        if (schemeResolver.HasCatalog)
        {
            await ValidateSchemesAsync(usage, options, schemeResolver, cancellationToken).ConfigureAwait(false);
        }

        if (usage is null)
        {
            return;
        }

        var policyNames = usage.PolicyNames;

        for (var i = 0; i < policyNames.Length; i++)
        {
            if (policyResolver.Resolve(policyNames[i], DirectiveNames.Policy.Name) is null)
            {
                throw ThrowHelper.PolicyNotResolved(DirectiveNames.Policy.Name, policyNames[i]);
            }
        }
    }

    private static async ValueTask ValidateSchemesAsync(
        FusionAuthorizationUsage? usage,
        FusionAuthorizationOptions options,
        AuthenticationSchemeResolver schemeResolver,
        CancellationToken cancellationToken)
    {
        var registered = await schemeResolver.GetRegisteredAsync(cancellationToken).ConfigureAwait(false);

        if (options.Schemes is { } listed)
        {
            for (var i = 0; i < listed.Length; i++)
            {
                if (!registered.Contains(listed[i]))
                {
                    throw ThrowHelper.AuthenticationSchemeNotRegistered(listed[i]);
                }
            }
        }

        var selected = options.Schemes ?? registered;

        if (usage is { IsUsed: true } && selected.IsEmpty)
        {
            throw ThrowHelper.NoAuthenticationSchemeRegistered();
        }
    }
}
