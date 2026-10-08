using System.Collections.Immutable;
using HotChocolate.Fusion.Authorization.Audit;

namespace HotChocolate.Fusion.Authorization;

internal sealed class FaultingAuditScope(IAuditScope inner, int failingCall, Exception failure) : IAuditScope
{
    private int _recordCalls;

    public IAuditTrail Trail => inner.Trail;

    public AuditScopeInfo Info => inner.Info;

    public AuditSubject Subject => inner.Subject;

    public ImmutableDictionary<string, string> Context => inner.Context;

    public bool IsRecording => inner.IsRecording;

    public void Record(in AuditLogEntry entry)
    {
        if (++_recordCalls == failingCall)
        {
            throw failure;
        }

        inner.Record(entry);
    }

    public void Fail(Exception exception) => inner.Fail(exception);

    public ValueTask CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);
}
