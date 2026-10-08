using System.Net;
using System.Text.Json;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using HotChocolate.Text.Json;
using Microsoft.Extensions.Primitives;

namespace HotChocolate.AspNetCore.Formatters;

public sealed class DefaultHttpResponseFormatterTests
{
    [Theory]
    [InlineData(HttpTransportVersion.Latest, HttpTransportVersion.Draft20250508)]
    [InlineData(HttpTransportVersion.Legacy, HttpTransportVersion.Legacy)]
    [InlineData(HttpTransportVersion.Draft20230127, HttpTransportVersion.Draft20250508)]
    [InlineData(HttpTransportVersion.Draft20250508, HttpTransportVersion.Draft20250508)]
    [InlineData(HttpTransportVersion.Draft20260903, HttpTransportVersion.Draft20260903)]
    public void Constructor_Should_ResolveTransportVersion_When_VersionIsRecognized(
        HttpTransportVersion configured,
        HttpTransportVersion expected)
    {
        // arrange
        var options = new HttpResponseFormatterOptions { HttpTransportVersion = configured };

        // act
        var formatter = new DefaultHttpResponseFormatter(options);

        // assert
        Assert.Equal(expected, formatter.TransportVersion);
    }

    [Fact]
    public void Constructor_Should_Throw_When_VersionIsUnrecognized()
    {
        // arrange
        var options = new HttpResponseFormatterOptions
        {
            HttpTransportVersion = (HttpTransportVersion)99
        };

        // act
        void Act() => _ = new DefaultHttpResponseFormatter(options);

        // assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(Act);
        Assert.Equal("options", exception.ParamName);
        Assert.Equal((HttpTransportVersion)99, exception.ActualValue);
        Assert.Equal(
            "The specified HTTP transport version `99` is not supported. (Parameter 'options')"
            + Environment.NewLine
            + "Actual value was 99.",
            exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task FormatAsync_Should_WriteRequestedStatus_When_EventStreamResultIsRequestErrorWithoutData(
        HttpStatusCode requested)
    {
        // arrange
        var formatter = new DefaultHttpResponseFormatter(new HttpResponseFormatterOptions());
        var context = new DefaultHttpContext();
        var result = OperationResult.FromError(ErrorBuilder.New().SetMessage("Denied.").Build());
        result.ContextData = result.ContextData.Add(ExecutionContextData.HttpStatusCode, requested);

        // act
        await formatter.FormatAsync(
            context.Response,
            result,
            [CreateEventStreamAcceptMediaType()],
            null,
            TestContext.Current.CancellationToken);

        // assert
        Assert.Equal((int)requested, context.Response.StatusCode);
    }

    [Fact]
    public async Task FormatAsync_Should_WriteOk_When_EventStreamResultHasDataAndRequestsAStatus()
    {
        // arrange
        var formatter = new DefaultHttpResponseFormatter(new HttpResponseFormatterOptions());
        var context = new DefaultHttpContext();
        var data = new Dictionary<string, object?> { ["probe"] = true };
        var result = new OperationResult(
            new OperationResultData(data, isValueNull: false, new DictionaryJsonFormatter(data), memoryHolder: null));
        result.ContextData = result.ContextData.Add(
            ExecutionContextData.HttpStatusCode,
            HttpStatusCode.Unauthorized);

        // act
        await formatter.FormatAsync(
            context.Response,
            result,
            [CreateEventStreamAcceptMediaType()],
            null,
            TestContext.Current.CancellationToken);

        // assert
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }

    private static AcceptMediaType CreateEventStreamAcceptMediaType()
        => new(new StringSegment("text"), new StringSegment("event-stream"), null, default);

    private sealed class DictionaryJsonFormatter(object value) : IRawJsonFormatter
    {
        private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

        public void WriteDataTo(JsonWriter jsonWriter)
            => JsonValueFormatter.WriteValue(jsonWriter, value, s_options);
    }
}
