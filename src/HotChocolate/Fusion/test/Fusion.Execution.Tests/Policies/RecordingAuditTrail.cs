using System.Collections.Immutable;
using System.Security.Claims;
using HotChocolate.Fusion.Policies.Audit;

namespace HotChocolate.Fusion.Policies;

internal sealed class RecordingAuditTrail(
    int index,
    RecordingAuditProvider provider,
    Func<int> countSourceSchemaRequests) : IAuditTrail
{
    public IAuditScope BeginRequest(AuditScopeInfo info, ClaimsPrincipal user)
    {
        var scope = new RecordingAuditScope(
            info,
            user,
            ImmutableDictionary<string, string>.Empty.Add("trail", index.ToString()),
            countSourceSchemaRequests);

        provider.AddScope(scope);
        return scope;
    }
}
