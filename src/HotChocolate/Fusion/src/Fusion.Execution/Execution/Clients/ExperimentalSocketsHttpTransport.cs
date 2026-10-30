namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// The <see cref="AppContext"/> switch and data that turn on and configure the experimental subgraph
/// transport, which sends source schema requests through handlers that Fusion owns instead of
/// through <see cref="IHttpClientFactory"/>.
/// </summary>
internal static class ExperimentalSocketsHttpTransport
{
    /// <summary>
    /// The name of the <see cref="AppContext"/> switch that turns the transport on.
    /// </summary>
    public const string SwitchName = "HotChocolate.Fusion.Experimental.SocketsHttpTransport";

    /// <summary>
    /// The name of the <see cref="AppContext"/> data entry that holds a comma-separated list of the
    /// gateway request headers that the transport forwards to source schemas.
    /// </summary>
    public const string ForwardedHeadersDataName = "HotChocolate.Fusion.Experimental.SocketsHttpTransport.ForwardedHeaders";

    /// <summary>
    /// Gets whether the transport is turned on.
    /// </summary>
    public static bool IsEnabled()
        => AppContext.TryGetSwitch(SwitchName, out var isEnabled) && isEnabled;

    /// <summary>
    /// Gets the names of the gateway request headers that the transport forwards to source schemas.
    /// </summary>
    public static string[] GetForwardedHeaders()
        => AppContext.GetData(ForwardedHeadersDataName) is string headers
            ? headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
