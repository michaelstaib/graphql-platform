using HotChocolate.Features;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// Copies headers of the gateway request onto the HTTP requests that the gateway sends to source schemas.
/// </summary>
internal interface ISourceSchemaHeaderForwarder
{
    /// <summary>
    /// Adds the forwarded headers of the gateway request that owns <paramref name="requestFeatures"/>
    /// to <paramref name="requestMessage"/>. Headers that <paramref name="requestMessage"/> already has are kept.
    /// </summary>
    /// <param name="requestFeatures">The features of the gateway request.</param>
    /// <param name="requestMessage">The HTTP request to a source schema.</param>
    void AddHeaders(IFeatureCollection requestFeatures, HttpRequestMessage requestMessage);
}
