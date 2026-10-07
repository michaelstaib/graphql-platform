using System.Security.Claims;

namespace HotChocolate.Fusion.Authorization.Audit;

internal sealed class NoOpAuditTrail : IAuditTrail
{
    public static NoOpAuditTrail Instance { get; } = new();

    private NoOpAuditTrail()
    {
    }

    public IAuditScope BeginRequest(AuditScopeInfo info, ClaimsPrincipal user) => NoOpAuditScope.Instance;
}
