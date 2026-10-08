using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Fusion.Authorization;

namespace HotChocolate.Fusion.AspNetCore;

/// <summary>
/// The default socket session interceptor of the gateway, which refuses a connection without an
/// authenticated identity when the authorization options reject unauthenticated requests.
/// </summary>
internal sealed class FusionSocketSessionInterceptor(FusionAuthorizationOptions options)
    : DefaultSocketSessionInterceptor
{
    public override ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default)
        => options.RejectRequestOn >= RejectRequestOn.OnUnauthenticated
            && session.Connection.HttpContext.User.Identity?.IsAuthenticated is not true
                ? new(ConnectionStatus.Unauthorized("Unauthorized"))
                : base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
}
