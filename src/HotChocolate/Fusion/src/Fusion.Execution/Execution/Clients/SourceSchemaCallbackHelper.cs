using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Transport.Http;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// Attaches the transport-level request hooks
/// (<see cref="HttpSourceSchemaClientConfiguration.OnBeforeSend"/> and
/// <see cref="HttpSourceSchemaClientConfiguration.OnAfterReceive"/>) to an HTTP request.
/// Shared by the HTTP source schema clients so both observe identical hook behavior.
/// </summary>
internal static class SourceSchemaCallbackHelper
{
    /// <summary>
    /// Attaches <see cref="HttpSourceSchemaClientConfiguration.OnBeforeSend"/> and
    /// <see cref="HttpSourceSchemaClientConfiguration.OnAfterReceive"/> callbacks to
    /// the HTTP request.
    /// </summary>
    /// <param name="request">The HTTP request the callbacks are attached to.</param>
    /// <param name="context">The current operation plan execution context.</param>
    /// <param name="node">The execution node that produced the request.</param>
    /// <param name="configuration">The transport configuration providing the callbacks.</param>
    public static void ConfigureCallbacks(
        GraphQLHttpRequest request,
        OperationPlanContext context,
        ExecutionNode node,
        HttpSourceSchemaClientConfiguration configuration)
    {
        var hasPropagatedHeaders = context.HasPropagatedHeaders;

        if (configuration.OnBeforeSend is null
            && configuration.OnAfterReceive is null
            && !hasPropagatedHeaders)
        {
            return;
        }

        request.State = new RequestCallbackState(context, node, configuration);

        if (configuration.OnBeforeSend is not null || hasPropagatedHeaders)
        {
            request.OnMessageCreated += static (_, requestMessage, state) =>
            {
                state.Configuration.OnBeforeSend?.Invoke(state.Context, state.Node, requestMessage);
                state.Context.AddPropagatedHeaders(requestMessage);
            };
        }

        if (configuration.OnAfterReceive is not null)
        {
            request.OnMessageReceived += static (_, responseMessage, state) =>
                state.Configuration.OnAfterReceive!.Invoke(state.Context, state.Node, responseMessage);
        }
    }
}
