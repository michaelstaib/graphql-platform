using HotChocolate.Execution;
using Microsoft.Extensions.Primitives;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// Provides the headers of the request that the gateway executes.
/// </summary>
internal interface IRequestHeaderProvider
{
    /// <summary>
    /// Gets the values of a header of the request.
    /// Empty when the request does not have the header.
    /// </summary>
    /// <param name="context">The context of the request.</param>
    /// <param name="name">The name of the header.</param>
    StringValues GetHeader(RequestContext context, string name);
}
