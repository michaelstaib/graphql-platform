using System.Collections.Immutable;
using System.Security.Claims;
using HotChocolate.Fusion.Authorization.Audit;

namespace HotChocolate.Fusion.Authorization;

internal sealed class RecordingAuditTrail(
    int index,
    RecordingAuditProvider provider,
    Func<int> countSourceSchemaRequests) : IAuditTrail
{
    public string InvocationId { get; } = "invocation-" + index;

    public IAuditScope BeginRequest(AuditScopeInfo info, ClaimsPrincipal user)
    {
        var scope = new RecordingAuditScope(
            this,
            info,
            user,
            ImmutableDictionary<string, string>.Empty.Add("trail", index.ToString()),
            countSourceSchemaRequests,
            provider.CommitFailure);

        provider.AddScope(scope);

        return provider.RecordFailure is { } recordFailure
            ? new FaultingAuditScope(scope, recordFailure.Call, recordFailure.Failure)
            : scope;
    }
}
