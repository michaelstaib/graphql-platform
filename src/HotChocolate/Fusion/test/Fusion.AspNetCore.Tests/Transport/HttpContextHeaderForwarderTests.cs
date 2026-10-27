using HotChocolate.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HotChocolate.Fusion.AspNetCore.Transport;

public sealed class HttpContextHeaderForwarderTests
{
    [Fact]
    public void AddHeaders_Should_CopyNamedHeaders_When_GatewayRequestHasThem()
    {
        // arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer user-a";
        httpContext.Request.Headers["X-Tenant"] = new StringValues(["a", "b"]);
        httpContext.Request.Headers["X-Not-Forwarded"] = "c";
        var features = new FeatureCollection();
        features.Set<HttpContext>(httpContext);
        var forwarder = new HttpContextHeaderForwarder(["Authorization", "X-Tenant", "X-Missing"]);
        using var requestMessage = new HttpRequestMessage();

        // act
        forwarder.AddHeaders(features, requestMessage);

        // assert
        requestMessage.Headers.ToString().MatchInlineSnapshot(
            """
            Authorization: Bearer user-a
            X-Tenant: a, b

            """);
    }

    [Fact]
    public void AddHeaders_Should_KeepHeader_When_SourceSchemaRequestHasIt()
    {
        // arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer user-a";
        var features = new FeatureCollection();
        features.Set<HttpContext>(httpContext);
        var forwarder = new HttpContextHeaderForwarder(["Authorization"]);
        using var requestMessage = new HttpRequestMessage();
        requestMessage.Headers.TryAddWithoutValidation("Authorization", "Bearer source-schema");

        // act
        forwarder.AddHeaders(features, requestMessage);

        // assert
        requestMessage.Headers.ToString().MatchInlineSnapshot(
            """
            Authorization: Bearer source-schema

            """);
    }

    [Fact]
    public void AddHeaders_Should_AddNothing_When_RequestHasNoHttpContext()
    {
        // arrange
        var forwarder = new HttpContextHeaderForwarder(["Authorization"]);
        using var requestMessage = new HttpRequestMessage();

        // act
        forwarder.AddHeaders(new FeatureCollection(), requestMessage);

        // assert
        Assert.Empty(requestMessage.Headers);
    }
}
