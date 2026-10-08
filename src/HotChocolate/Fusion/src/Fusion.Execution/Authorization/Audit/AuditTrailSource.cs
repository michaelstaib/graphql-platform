namespace HotChocolate.Fusion.Authorization.Audit;

/// <summary>
/// Creates the audit trail of a request batch on first use and shares it between the requests.
/// </summary>
internal sealed class AuditTrailSource
{
    private readonly object _sync = new();
    private IAuditTrail? _trail;

    /// <summary>
    /// Gets the trail of the batch and creates it with the provider when it does not exist yet.
    /// </summary>
    /// <param name="provider">
    /// The provider that creates the trail.
    /// </param>
    /// <param name="requestServices">
    /// The request scoped service provider of the batch.
    /// </param>
    public IAuditTrail GetOrCreate(IAuditProvider provider, IServiceProvider requestServices)
    {
        lock (_sync)
        {
            return _trail ??= provider.CreateTrail(requestServices);
        }
    }
}
