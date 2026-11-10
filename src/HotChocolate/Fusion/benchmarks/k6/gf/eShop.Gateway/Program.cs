using eShop.Gateway;
using eShop.Gateway.BenchmarkDiagnostics;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("BENCH_DIAGNOSTIC_RUN") == "1")
{
    builder.Services.AddHostedService<FusionPoolDiagnostics>();
}

// Opt-in MemoryArena lifecycle tracing: enabled only when FUSION_ARENA_TRACE holds an output path.
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FUSION_ARENA_TRACE")))
{
    builder.Services.AddHostedService<MemoryArenaEventListener>();
}

// BENCH_FUSION_SOCKETS_TRANSPORT=1 sends subgraph requests through the experimental transport, which owns
// its handlers and forwards Authorization itself, instead of through the "fusion" IHttpClientFactory client.
var useHttpClientFactory = Environment.GetEnvironmentVariable("BENCH_FUSION_SOCKETS_TRANSPORT") != "1";

if (useHttpClientFactory)
{
    builder.Services
        .AddHeaderPropagation(options => options.Headers.Add("Authorization"));

    builder.Services
        .AddHttpClient("fusion")
        // Drop the default LoggingHttpMessageHandler and LoggingScopeHttpMessageHandler. Even with
        // logging disabled they add two async state machines and a scope per subgraph call.
        .RemoveAllLoggers()
        .ConfigurePrimaryHttpMessageHandler(
            () => new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 256
            })
        .AddHeaderPropagation();
}
else
{
    AppContext.SetSwitch("HotChocolate.Fusion.Experimental.SocketsHttpTransport", true);
    AppContext.SetData("HotChocolate.Fusion.Experimental.SocketsHttpTransport.ForwardedHeaders", "Authorization");
}

builder
    .AddGraphQLGateway(disableDefaultSecurity: true)
    .AddFileSystemConfiguration("./gateway.far");

var app = builder.Build();

if (useHttpClientFactory)
{
    app.UseHeaderPropagation();
}

app.MapGraphQLHttp();

app.Run();
