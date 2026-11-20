using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HotChocolate.Fusion.AspNetCore;

internal sealed class HttpContextRequestHeaderProvider : IRequestHeaderProvider
{
    public StringValues GetHeader(RequestContext context, string name)
        => context.ContextData.TryGetValue(nameof(HttpContext), out var value)
            && value is HttpContext httpContext
                ? httpContext.Request.Headers[name]
                : StringValues.Empty;
}
