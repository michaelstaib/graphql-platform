using HotChocolate.Execution;
using HotChocolate.Fusion.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution;

public class FusionOptionsTests : FusionTestBase
{
    /// <summary>
    /// Verifies that setting <c>EnableOptInFeatures</c> via <c>ModifyOptions</c> results in
    /// <see cref="IFusionSchemaOptions.EnableOptInFeatures"/> being <c>true</c> on the
    /// built schema's feature collection.
    /// </summary>
    [Fact]
    public async Task EnableOptInFeatures_SetsOption()
    {
        // arrange
        var services = new ServiceCollection();
        services
            .AddGraphQLGateway()
            .ModifyOptions(o => o.EnableOptInFeatures = true)
            .AddInMemoryConfiguration(
                ComposeSchemaDocument(
                    """
                    type Query {
                        field: String!
                    }
                    """));

        // act
        IServiceProvider serviceProvider = services.BuildServiceProvider();
        var executor = await serviceProvider.GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        var options = executor.Schema.Features.Get<IFusionSchemaOptions>();
        Assert.NotNull(options);
        Assert.True(options.EnableOptInFeatures);
    }

    [Fact]
    public void Clone_CopiesConfiguredValues()
    {
        // arrange
        var options = new FusionOptions
        {
            EvictionTimeout = TimeSpan.FromSeconds(90),
            OperationDocumentCacheSize = 1024,
            EnableDefer = false,
            EnableObjectDeprecation = true,
            EnableEntityReuse = false,
            PropagatedHeaders = ["Authorization"]
        };

        // act
        var clone = options.Clone();

        // assert
        clone.MatchInlineSnapshot(
            """
            {
              "EvictionTimeout": "00:01:30",
              "OperationExecutionPlanCacheSize": 256,
              "OperationExecutionPlanCacheDiagnostics": null,
              "OperationDocumentCacheSize": 1024,
              "PathSegmentLocalPoolCapacity": 64,
              "EnableEntityReuse": false,
              "PropagatedHeaders": [
                "Authorization"
              ],
              "LazyInitialization": false,
              "NodeIdSerializerFormat": "Base64",
              "ApplySerializeAsToScalars": false,
              "EnableDefer": false,
              "EnableObjectDeprecation": true,
              "EnableOptInFeatures": false,
              "EnableEmptySelectionSets": true,
              "EnableSemanticIntrospection": true
            }
            """);
    }

    [Theory]
    [InlineData("Content-Length")]
    [InlineData("content-type")]
    [InlineData("Content-Encoding")]
    [InlineData("Transfer-Encoding")]
    [InlineData("connection")]
    [InlineData("Keep-Alive")]
    [InlineData("Upgrade")]
    [InlineData("TE")]
    [InlineData("Trailer")]
    public void PropagatedHeaders_Should_Throw_When_HeaderDescribesBodyOrConnection(string headerName)
    {
        // arrange
        var options = new FusionOptions();

        // act
        var exception = Record.Exception(() => options.PropagatedHeaders = ["Authorization", headerName]);

        // assert
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Equal(
            $"The header '{headerName}' describes the body or the connection of a single request "
            + "and cannot be copied to source schema requests. (Parameter 'value')",
            argumentException.Message);
        Assert.Empty(options.PropagatedHeaders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("X Tenant")]
    [InlineData("X-Tenant:")]
    public void PropagatedHeaders_Should_Throw_When_HeaderNameIsInvalid(string headerName)
    {
        // arrange
        var options = new FusionOptions();

        // act
        var exception = Record.Exception(() => options.PropagatedHeaders = [headerName]);

        // assert
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Equal(
            $"'{headerName}' is not a valid HTTP header name. (Parameter 'value')",
            argumentException.Message);
    }

    [Fact]
    public void PropagatedHeaders_Should_KeepNames_When_HeadersAreRequestHeaders()
    {
        // arrange
        string[] headerNames = ["Authorization", "X-Tenant", "traceparent", "Cookie"];
        var options = new FusionOptions();

        // act
        options.PropagatedHeaders = [.. headerNames];

        // assert
        Assert.Equal(headerNames, options.PropagatedHeaders);
    }

    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(-1.0, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(0.0, true)]
    [InlineData(10.0, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void MaxFieldCost_Should_ValidateDomain_When_SetOnFusionOptions(
        double value,
        bool isValid)
    {
        // arrange
        var options = new FusionCostOptions();

        // act
        var exception = Record.Exception(() => options.MaxFieldCost = value);

        // assert
        if (isValid)
        {
            Assert.Null(exception);
            Assert.Equal(value, options.MaxFieldCost);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Equal(1_000, options.MaxFieldCost);
        }
    }

    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(-1.0, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(0.0, true)]
    [InlineData(10.0, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void MaxTypeCost_Should_ValidateDomain_When_SetOnFusionOptions(
        double value,
        bool isValid)
    {
        // arrange
        var options = new FusionCostOptions();

        // act
        var exception = Record.Exception(() => options.MaxTypeCost = value);

        // assert
        if (isValid)
        {
            Assert.Null(exception);
            Assert.Equal(value, options.MaxTypeCost);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Equal(10_000, options.MaxTypeCost);
        }
    }

    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(-1.0, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(null, true)]
    [InlineData(0.0, true)]
    [InlineData(10.0, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void MaxResponseSize_Should_ValidateDomain_When_SetOnFusionOptions(
        double? value,
        bool isValid)
    {
        // arrange
        var options = new FusionCostOptions();

        // act
        var exception = Record.Exception(() => options.MaxResponseSize = value);

        // assert
        if (isValid)
        {
            Assert.Null(exception);
            Assert.Equal(value, options.MaxResponseSize);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Null(options.MaxResponseSize);
        }
    }
}
