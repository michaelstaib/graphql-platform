using System.Net.Http.Headers;
using HotChocolate.Fusion.Execution.Nodes;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// Carries the context needed by the transport-level request hooks
/// (<see cref="HttpSourceSchemaClientConfiguration.OnBeforeSend"/> and
/// <see cref="HttpSourceSchemaClientConfiguration.OnAfterReceive"/>).
/// Stored on <see cref="Transport.Http.GraphQLHttpRequest.State"/>
/// so the static hook delegates can access it without capturing.
/// </summary>
public readonly struct RequestCallbackState
{
    public RequestCallbackState(
        OperationPlanContext context,
        ExecutionNode node,
        HttpSourceSchemaClientConfiguration configuration)
        : this(context, node, configuration, defaultRequestHeaders: null)
    {
    }

    internal RequestCallbackState(
        OperationPlanContext context,
        ExecutionNode node,
        HttpSourceSchemaClientConfiguration configuration,
        HttpRequestHeaders? defaultRequestHeaders)
    {
        Context = context;
        Node = node;
        Configuration = configuration;
        DefaultRequestHeaders = defaultRequestHeaders;
    }

    public OperationPlanContext Context { get; }

    public ExecutionNode Node { get; }

    public HttpSourceSchemaClientConfiguration Configuration { get; }

    /// <summary>
    /// Gets the headers that the HTTP client adds to a request that does not have them,
    /// or <c>null</c> when they are not needed or the HTTP client adds none.
    /// </summary>
    internal HttpRequestHeaders? DefaultRequestHeaders { get; }
}
