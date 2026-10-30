using HotChocolate.Features;
using HotChocolate.Fusion.Execution.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HotChocolate.Fusion.AspNetCore.Transport;

/// <summary>
/// Copies the named headers of the gateway <see cref="HttpContext"/> request onto source schema requests.
/// </summary>
/// <param name="headerNames">The names of the headers to copy.</param>
internal sealed class HttpContextHeaderForwarder(string[] headerNames) : ISourceSchemaHeaderForwarder
{
    public void AddHeaders(IFeatureCollection requestFeatures, HttpRequestMessage requestMessage)
    {
        if (headerNames.Length == 0 || !requestFeatures.TryGet<HttpContext>(out var httpContext))
        {
            return;
        }

        var incoming = httpContext.Request.Headers;
        var outgoing = requestMessage.Headers;

        foreach (var name in headerNames)
        {
            if (!incoming.TryGetValue(name, out var values)
                || StringValues.IsNullOrEmpty(values)
                || outgoing.Contains(name))
            {
                continue;
            }

            if (values.Count == 1)
            {
                outgoing.TryAddWithoutValidation(name, values.ToString());
            }
            else
            {
                outgoing.TryAddWithoutValidation(name, values.ToArray());
            }
        }
    }
}
