namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// The connection settings of the <see cref="SocketsHttpHandler"/> that sends the requests of one source schema.
/// Two settings instances with equal values create equivalent handlers.
/// </summary>
/// <param name="ConnectTimeout">The time to wait for a connection to be established.</param>
/// <param name="PooledConnectionLifetime">
/// The time after which a pooled connection is closed instead of reused.
/// </param>
/// <param name="PooledConnectionIdleTimeout">The time an idle pooled connection stays open.</param>
/// <param name="MaxConnectionsPerServer">The maximum number of connections to one server.</param>
/// <param name="EnableMultipleHttp2Connections">
/// Whether more than one HTTP/2 connection to a server may be opened.
/// </param>
internal sealed record SocketsHttpTransportSettings(
    TimeSpan ConnectTimeout,
    TimeSpan PooledConnectionLifetime,
    TimeSpan PooledConnectionIdleTimeout,
    int MaxConnectionsPerServer,
    bool EnableMultipleHttp2Connections)
{
    /// <summary>
    /// Gets the settings that the transport uses for every source schema.
    /// </summary>
    public static SocketsHttpTransportSettings Default { get; } = new(
        ConnectTimeout: TimeSpan.FromSeconds(15),
        PooledConnectionLifetime: TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout: TimeSpan.FromMinutes(1),
        MaxConnectionsPerServer: int.MaxValue,
        EnableMultipleHttp2Connections: true);

    /// <summary>
    /// Creates a handler with these settings that neither stores cookies, follows redirects nor
    /// decompresses responses.
    /// </summary>
    public SocketsHttpHandler CreateHandler()
        => new()
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = PooledConnectionLifetime,
            PooledConnectionIdleTimeout = PooledConnectionIdleTimeout,
            MaxConnectionsPerServer = MaxConnectionsPerServer,
            EnableMultipleHttp2Connections = EnableMultipleHttp2Connections
        };
}
