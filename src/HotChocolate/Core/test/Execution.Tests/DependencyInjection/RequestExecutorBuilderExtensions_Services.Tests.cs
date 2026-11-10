using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace HotChocolate.Execution.DependencyInjection;

public class RequestExecutorBuilderExtensionsServicesTests
{
    [Fact]
    public void AddScopedServiceInitializer_1_Builder_Is_Null()
    {
        void Fail() => RequestExecutorBuilderExtensions
            .AddScopedServiceInitializer<string>(null!, (_, _) => { });

        Assert.Throws<ArgumentNullException>(Fail);
    }

    [Fact]
    public void AddScopedServiceInitializer_1_Initializer_Is_Null()
    {
        var mock = new Mock<IRequestExecutorBuilder>();

        void Fail() => RequestExecutorBuilderExtensions
            .AddScopedServiceInitializer<string>(mock.Object, null!);

        Assert.Throws<ArgumentNullException>(Fail);
    }

    [Fact]
    public async Task AddGraphQL_Should_RegisterSystemTimeProvider_When_NoTimeProviderIsRegistered()
    {
        // arrange
        var services = new ServiceCollection();
        services.AddGraphQL().AddQueryType(d => d.Field("foo").Resolve("bar"));

        // act
        var executor = await services.BuildServiceProvider().GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Same(TimeProvider.System, executor.Schema.Services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task AddGraphQL_Should_ResolveRegisteredTimeProvider_When_TimeProviderIsAddedToSchemaServices()
    {
        // arrange
        var timeProvider = new FakeTimeProvider();
        var services = new ServiceCollection();
        services
            .AddGraphQL()
            .AddQueryType(d => d.Field("foo").Resolve("bar"))
            .ConfigureSchemaServices(s => s.AddSingleton<TimeProvider>(timeProvider));

        // act
        var executor = await services.BuildServiceProvider().GetRequestExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Same(timeProvider, executor.Schema.Services.GetRequiredService<TimeProvider>());
    }
}
