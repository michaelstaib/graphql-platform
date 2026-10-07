using System.Collections.Concurrent;
using HotChocolate.Fusion.Authorization.Audit;

namespace HotChocolate.Fusion.Authorization;

internal sealed class RecordingAuditProvider(AuthorizationTestClient client) : IAuditProvider
{
    private readonly ConcurrentQueue<RecordingAuditScope> _scopes = [];
    private int _trailCount;

    public int TrailCount => _trailCount;

    public IReadOnlyCollection<RecordingAuditScope> Scopes => _scopes;

    public IAuditTrail CreateTrail(IServiceProvider requestServices)
        => new RecordingAuditTrail(
            Interlocked.Increment(ref _trailCount),
            this,
            () => client.Requests.Count);

    public void AddScope(RecordingAuditScope scope) => _scopes.Enqueue(scope);
}
