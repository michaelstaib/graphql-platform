using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HotChocolate.Fusion.Execution.Clients;

public sealed class SocketsHttpSourceSchemaClientFactoryTests
{
    [Fact]
    public async Task GetHandler_Should_NotSendCookie_When_SourceSchemaSetCookieOnEarlierResponse()
    {
        // arrange
        await using var server = await SourceSchemaServer.StartAsync();
        using var factory = CreateFactory();
        using var invoker = new HttpMessageInvoker(
            factory.GetHandler(CreateConfiguration("a", server.BaseAddress)),
            disposeHandler: false);

        // act
        using var first = await PostAsync(invoker, server.CreateUri("/set-cookie"));
        using var second = await PostAsync(invoker, server.CreateUri("/graphql"));

        // assert
        server.Requests.MatchInlineSnapshots(
        [
            "POST /set-cookie cookie=",
            "POST /graphql cookie="
        ]);
    }

    [Fact]
    public async Task GetHandler_Should_ReturnRedirect_When_SourceSchemaRedirects()
    {
        // arrange
        await using var server = await SourceSchemaServer.StartAsync();
        using var factory = CreateFactory();
        using var invoker = new HttpMessageInvoker(
            factory.GetHandler(CreateConfiguration("a", server.BaseAddress)),
            disposeHandler: false);

        // act
        using var response = await PostAsync(invoker, server.CreateUri("/redirect"));

        // assert
        $"{(int)response.StatusCode} {response.Headers.Location}; requests: {string.Join(", ", server.Requests)}"
            .MatchInlineSnapshot("307 /graphql; requests: POST /redirect cookie=");
    }

    [Fact]
    public async Task GetHandler_Should_ReturnEncodedBody_When_SourceSchemaCompressesResponse()
    {
        // arrange
        await using var server = await SourceSchemaServer.StartAsync();
        using var factory = CreateFactory();
        using var invoker = new HttpMessageInvoker(
            factory.GetHandler(CreateConfiguration("a", server.BaseAddress)),
            disposeHandler: false);

        // act
        using var response = await PostAsync(invoker, server.CreateUri("/gzip"));
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // assert
        $"{string.Join(",", response.Content.Headers.ContentEncoding)} gzip-magic={body is [0x1f, 0x8b, ..]}"
            .MatchInlineSnapshot("gzip gzip-magic=True");
    }

    [Fact]
    public void GetHandler_Should_ReuseHandler_When_ReloadedSourceSchemaHasSameSettings()
    {
        // arrange
        var baseAddress = new Uri("http://localhost:5000/graphql");
        using var factory = CreateFactory();

        // act
        var handler = factory.GetHandler(CreateConfiguration("a", baseAddress));
        var reloadedHandler = factory.GetHandler(CreateConfiguration("a", baseAddress));
        var otherHandler = factory.GetHandler(CreateConfiguration("b", baseAddress));

        // assert
        Assert.Same(handler, reloadedHandler);
        Assert.NotSame(handler, otherHandler);
    }

    [Fact]
    public void GetHandler_Should_CreateHandler_When_ReloadedSourceSchemaHasOtherSettings()
    {
        // arrange
        var settings = SocketsHttpTransportSettings.Default;
        var baseAddress = new Uri("http://localhost:5000/graphql");
        using var factory = new SocketsHttpSourceSchemaClientFactory(_ => settings, headerForwarder: null);
        var handler = factory.GetHandler(CreateConfiguration("a", baseAddress));

        // act
        settings = settings with { MaxConnectionsPerServer = 16 };
        var reloadedHandler = factory.GetHandler(CreateConfiguration("a", baseAddress));

        // assert
        Assert.NotSame(handler, reloadedHandler);
        Assert.Equal(16, Assert.IsType<SocketsHttpHandler>(reloadedHandler).MaxConnectionsPerServer);
    }

    private static SocketsHttpSourceSchemaClientFactory CreateFactory()
        => new(_ => SocketsHttpTransportSettings.Default, headerForwarder: null);

    private static HttpSourceSchemaClientConfiguration CreateConfiguration(string name, Uri baseAddress)
        => new(name, baseAddress);

    private static async Task<HttpResponseMessage> PostAsync(HttpMessageInvoker invoker, Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent("""{"query":"{ __typename }"}""")
        };

        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class SourceSchemaServer : IAsyncDisposable
    {
        private readonly Lock _sync = new();
        private readonly List<string> _requests = [];
        private readonly WebApplication _app;

        private SourceSchemaServer(WebApplication app)
        {
            _app = app;
        }

        public Uri BaseAddress { get; private set; } = null!;

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_sync)
                {
                    return _requests.ToArray();
                }
            }
        }

        public Uri CreateUri(string path) => new(BaseAddress, path);

        public static async Task<SourceSchemaServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();
            var server = new SourceSchemaServer(app);

            app.Run(async context =>
            {
                lock (server._sync)
                {
                    server._requests.Add(
                        $"{context.Request.Method} {context.Request.Path} cookie={context.Request.Headers.Cookie}");
                }

                switch (context.Request.Path.Value)
                {
                    case "/set-cookie":
                        context.Response.Headers.SetCookie = "session=user-a; Path=/";
                        break;

                    case "/redirect":
                        context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                        context.Response.Headers.Location = "/graphql";
                        return;

                    case "/gzip":
                        context.Response.ContentType = "application/graphql-response+json";
                        context.Response.Headers.ContentEncoding = "gzip";
                        await using (var gzip = new GZipStream(context.Response.Body, CompressionLevel.Fastest))
                        {
                            await gzip.WriteAsync("""{"data":{"__typename":"Query"}}"""u8.ToArray());
                        }

                        return;
                }

                context.Response.ContentType = "application/graphql-response+json";
                await context.Response.WriteAsync("""{"data":{"__typename":"Query"}}""");
            });

            await app.StartAsync();
            server.BaseAddress = new Uri(app.Urls.First());
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
