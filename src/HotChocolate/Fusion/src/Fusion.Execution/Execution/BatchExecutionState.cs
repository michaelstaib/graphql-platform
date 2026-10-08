using System.Collections.Concurrent;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization.Audit;

namespace HotChocolate.Fusion.Execution;

/// <summary>
/// The results that the items of a request batch unwrap into. It also holds the audit trail that
/// the protected items of the batch share.
/// </summary>
internal sealed class BatchExecutionState : ConcurrentQueue<IExecutionResult>
{
    private object? _auditTrailSync;
    private IAuditTrail? _auditTrail;

    /// <summary>
    /// Gets the audit trail of the batch and creates it with the provider when no item has done so.
    /// </summary>
    /// <param name="provider">
    /// The provider that creates the trail.
    /// </param>
    /// <param name="requestServices">
    /// The request scoped service provider of the batch.
    /// </param>
    public IAuditTrail GetOrCreateAuditTrail(IAuditProvider provider, IServiceProvider requestServices)
    {
        var trail = Volatile.Read(ref _auditTrail);

        if (trail is not null)
        {
            return trail;
        }

        var created = new object();
        var sync = Interlocked.CompareExchange(ref _auditTrailSync, created, null) ?? created;

        lock (sync)
        {
            trail = _auditTrail ??= provider.CreateTrail(requestServices);
        }

        return trail;
    }
}
