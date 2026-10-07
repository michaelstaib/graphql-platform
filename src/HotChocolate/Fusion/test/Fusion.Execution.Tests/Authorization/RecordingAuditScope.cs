using System.Collections.Immutable;
using System.Security.Claims;
using HotChocolate.Fusion.Authorization.Audit;

namespace HotChocolate.Fusion.Authorization;

internal sealed class RecordingAuditScope(
    AuditScopeInfo info,
    ClaimsPrincipal user,
    ImmutableDictionary<string, string> context,
    Func<int> countSourceSchemaRequests)
    : AuditScope(info, user, context)
{
    public ImmutableArray<AuditLogEntry> Entries { get; private set; }

    public int CommitCount { get; private set; }

    public int SourceSchemaRequestsAtCommit { get; private set; }

    protected override ValueTask OnCommitAsync(
        ImmutableArray<AuditLogEntry> entries,
        CancellationToken cancellationToken)
    {
        Entries = entries;
        CommitCount++;
        SourceSchemaRequestsAtCommit = countSourceSchemaRequests();
        return ValueTask.CompletedTask;
    }
}
