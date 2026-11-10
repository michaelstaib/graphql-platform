using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using HotChocolate.Execution;
using HotChocolate.Fusion.Transport.Http;
using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// Creates HTTP source schema clients that send through one <see cref="SocketsHttpHandler"/> per source
/// schema, which this factory owns and keeps across schema reloads while the source schema's settings
/// stay the same.
/// </summary>
internal sealed class SocketsHttpSourceSchemaClientFactory
    : SourceSchemaClientFactory<HttpSourceSchemaClientConfiguration>
    , ISharedSourceSchemaClientFactory
    , IDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly ConditionalWeakTable<HttpSourceSchemaClientConfiguration, SharedClientEntry> _sharedClients = [];
    private readonly Dictionary<string, Transport> _transports = new(StringComparer.Ordinal);
    private readonly Func<HttpSourceSchemaClientConfiguration, SocketsHttpTransportSettings> _getSettings;
    private readonly ISourceSchemaHeaderForwarder? _headerForwarder;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="SocketsHttpSourceSchemaClientFactory"/>.
    /// </summary>
    /// <param name="getSettings">Gets the handler settings of a source schema.</param>
    /// <param name="headerForwarder">
    /// Adds gateway request headers to source schema requests, or <c>null</c> to forward none.
    /// </param>
    public SocketsHttpSourceSchemaClientFactory(
        Func<HttpSourceSchemaClientConfiguration, SocketsHttpTransportSettings> getSettings,
        ISourceSchemaHeaderForwarder? headerForwarder)
    {
        ArgumentNullException.ThrowIfNull(getSettings);

        _getSettings = getSettings;
        _headerForwarder = headerForwarder;
    }

    protected override ISourceSchemaClient CreateClient(
        FusionSchemaDefinition schema,
        HttpSourceSchemaClientConfiguration configuration)
    {
        lock (_sync)
        {
            return CreateClientCore(schema, configuration);
        }
    }

    public bool TryGetSharedClient(
        FusionSchemaDefinition schema,
        ISourceSchemaClientConfiguration configuration,
        [NotNullWhen(true)] out SharedSourceSchemaClient? client)
    {
        if (configuration is not HttpSourceSchemaClientConfiguration httpConfiguration)
        {
            client = null;
            return false;
        }

        var annotateOperationKind = schema.GetRequestOptions().AnnotateOperationKind;

        if (!_sharedClients.TryGetValue(httpConfiguration, out var entry)
            || entry.AnnotateOperationKind != annotateOperationKind)
        {
            lock (_sync)
            {
                if (!_sharedClients.TryGetValue(httpConfiguration, out entry)
                    || entry.AnnotateOperationKind != annotateOperationKind)
                {
                    // The handler outlives the client, so the shared client never expires.
                    var sharedClient = new SharedSourceSchemaClient(
                        CreateClientCore(schema, httpConfiguration),
                        long.MaxValue);
                    entry = new SharedClientEntry(sharedClient, annotateOperationKind);
                    _sharedClients.AddOrUpdate(httpConfiguration, entry);
                }
            }
        }

        client = entry.Client;
        return true;
    }

    /// <summary>
    /// Gets the handler that sends the requests of the source schema of <paramref name="configuration"/>.
    /// </summary>
    internal HttpMessageHandler GetHandler(HttpSourceSchemaClientConfiguration configuration)
    {
        lock (_sync)
        {
            return GetHandlerCore(configuration);
        }
    }

    private HttpSourceSchemaClient CreateClientCore(
        FusionSchemaDefinition schema,
        HttpSourceSchemaClientConfiguration configuration)
    {
        var handler = GetHandlerCore(configuration);

        // Requests with an execution deadline go straight to the handler. All other requests keep the
        // time-to-response-headers bound of the HttpClient timeout.
        var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = configuration.BaseAddress };

        return new HttpSourceSchemaClient(
            new DefaultGraphQLHttpClient(httpClient, handler, _headerForwarder),
            configuration,
            schema.GetRequestOptions().AnnotateOperationKind);
    }

    private HttpMessageHandler GetHandlerCore(HttpSourceSchemaClientConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var settings = _getSettings(configuration);

        if (_transports.TryGetValue(configuration.Name, out var transport) && transport.Settings == settings)
        {
            return transport.Handler;
        }

        // The handler this one replaces is not disposed, because requests that took it before the schema
        // reload may still be sending with it. The garbage collector reclaims it with its connections.
        var handler = settings.CreateHandler();
        _transports[configuration.Name] = new Transport(settings, handler);
        return handler;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var transport in _transports.Values)
            {
                transport.Handler.Dispose();
            }

            _transports.Clear();
        }
    }

    private sealed record Transport(SocketsHttpTransportSettings Settings, SocketsHttpHandler Handler);

    private sealed record SharedClientEntry(SharedSourceSchemaClient Client, bool AnnotateOperationKind);
}
