using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Execution;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class AspNetCoreFusionGatewayBuilderExtensions
{
    /// <summary>
    /// Copies the specified headers of an incoming request to the requests that the gateway sends
    /// to source schemas. A header that a source schema request already has is kept.
    /// </summary>
    /// <param name="builder">
    /// The <see cref="IFusionGatewayBuilder"/>.
    /// </param>
    /// <param name="headerNames">
    /// The names of the headers.
    /// </param>
    /// <returns>
    /// Returns the <see cref="IFusionGatewayBuilder"/> so that configuration can be chained.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// A name is not a valid header name, or it names a header that describes the body
    /// or the connection of a single request.
    /// </exception>
    public static IFusionGatewayBuilder AddHeaderPropagation(
        this IFusionGatewayBuilder builder,
        params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(headerNames);

        // The options are modified later, so the caller's array is copied here.
        string[] names = [.. headerNames];

        foreach (var headerName in names)
        {
            FusionOptions.ValidatePropagatedHeader(headerName, nameof(headerNames));
        }

        return builder.ModifyOptions(
            options =>
            {
                var propagatedHeaders = options.PropagatedHeaders;

                foreach (var headerName in names)
                {
                    if (!propagatedHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase))
                    {
                        propagatedHeaders = propagatedHeaders.Add(headerName);
                    }
                }

                options.PropagatedHeaders = propagatedHeaders;
            });
    }
}
