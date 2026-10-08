using System.Collections.Concurrent;
using System.Text.Json;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution;

public class FusionRequestExecutorManagerUpdateTests : FusionTestBase
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Update_Should_KeepPreviousExecutorAndApplyLaterConfiguration_When_ExecutorCreationThrows()
    {
        // arrange
        var failCreation = false;
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, _) =>
                {
                    if (failCreation)
                    {
                        throw new InvalidOperationException("creation failed");
                    }
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        var initialExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var created = ObserveCreatedExecutors(manager);

        // act
        failCreation = true;
        configProvider.UpdateConfiguration(CreateConfiguration("rejected"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterFailure = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        failCreation = false;
        configProvider.UpdateConfiguration(CreateConfiguration("accepted"));
        var executorAfterRecovery = await created.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var currentExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Same(initialExecutor, executorAfterFailure);
        Assert.Equal(ISchemaDefinition.DefaultName, failure.SchemaName);
        Assert.Equal("creation failed", failure.Exception.Message);
        Assert.True(executorAfterRecovery.Schema.QueryType.Fields.ContainsName("accepted"));
        Assert.Same(executorAfterRecovery, currentExecutor);
    }

    [Fact]
    public async Task Update_Should_ApplyRetriedConfiguration_When_IdenticalConfigurationFailedBefore()
    {
        // arrange
        var failWarmup = false;
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .AddWarmupTask((_, _) =>
                {
                    return failWarmup
                        ? throw new InvalidOperationException("warmup failed")
                        : Task.CompletedTask;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        var initialExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var created = ObserveCreatedExecutors(manager);

        // act
        failWarmup = true;
        configProvider.UpdateConfiguration(CreateConfiguration("retried"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        failWarmup = false;
        configProvider.UpdateConfiguration(CreateConfiguration("retried"));
        var executorAfterRetry = await created.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal("warmup failed", failure.Exception.Message);
        Assert.NotSame(initialExecutor, executorAfterRetry);
        Assert.True(executorAfterRetry.Schema.QueryType.Fields.ContainsName("retried"));
    }

    [Fact]
    public async Task Update_Should_DisposeRejectedExecutorAndConfiguration_When_WarmupThrows()
    {
        // arrange
        var failWarmup = false;
        DisposalProbe? rejectedProbe = null;
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<DisposalProbe>())
                .AddWarmupTask((executor, _) =>
                {
                    if (failWarmup)
                    {
                        rejectedProbe = executor.Schema.Services.GetRequiredService<DisposalProbe>();
                        throw new InvalidOperationException("warmup failed");
                    }

                    return Task.CompletedTask;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var rejectedConfiguration = CreateConfiguration("rejected");

        // act
        failWarmup = true;
        configProvider.UpdateConfiguration(rejectedConfiguration);
        await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await rejectedProbe!.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Throws<ObjectDisposedException>(() => rejectedConfiguration.Settings.Document.RootElement.ValueKind);
    }

    [Fact]
    public async Task Update_Should_ReportFailureAndApplyLaterConfiguration_When_HashingTheConfigurationThrows()
    {
        // arrange
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        var initialExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var created = ObserveCreatedExecutors(manager);
        var unreadableConfiguration = CreateConfiguration("unreadable");
        unreadableConfiguration.Dispose();

        // act
        configProvider.UpdateConfiguration(unreadableConfiguration);
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterFailure = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        configProvider.UpdateConfiguration(CreateConfiguration("accepted"));
        var executorAfterRecovery = await created.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ObjectDisposedException>(failure.Exception);
        Assert.Same(initialExecutor, executorAfterFailure);
        Assert.True(executorAfterRecovery.Schema.QueryType.Fields.ContainsName("accepted"));
    }

    [Fact]
    public async Task Update_Should_DisposeRejectedExecutorAndApplyLaterConfiguration_When_FailureListenerThrows()
    {
        // arrange
        var failWarmup = false;
        DisposalProbe? rejectedProbe = null;
        var listener = new ThrowingUpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<DisposalProbe>())
                .AddWarmupTask((executor, _) =>
                {
                    if (failWarmup)
                    {
                        rejectedProbe = executor.Schema.Services.GetRequiredService<DisposalProbe>();
                        throw new InvalidOperationException("warmup failed");
                    }

                    return Task.CompletedTask;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var created = ObserveCreatedExecutors(manager);

        // act
        failWarmup = true;
        configProvider.UpdateConfiguration(CreateConfiguration("rejected"));
        await listener.Invoked.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await rejectedProbe!.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        failWarmup = false;
        configProvider.UpdateConfiguration(CreateConfiguration("accepted"));
        var executorAfterRecovery = await created.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.True(executorAfterRecovery.Schema.QueryType.Fields.ContainsName("accepted"));
    }

    [Fact]
    public async Task Update_Should_ApplyLaterConfiguration_When_DisposingTheRejectedExecutorThrows()
    {
        // arrange
        var failWarmup = false;
        ThrowingDisposable? rejectedDisposable = null;
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<ThrowingDisposable>())
                .AddWarmupTask((executor, _) =>
                {
                    if (failWarmup)
                    {
                        rejectedDisposable = executor.Schema.Services.GetRequiredService<ThrowingDisposable>();
                        throw new InvalidOperationException("warmup failed");
                    }

                    return Task.CompletedTask;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var created = ObserveCreatedExecutors(manager);

        // act
        failWarmup = true;
        configProvider.UpdateConfiguration(CreateConfiguration("rejected"));
        await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await rejectedDisposable!.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        failWarmup = false;
        configProvider.UpdateConfiguration(CreateConfiguration("accepted"));
        var executorAfterRecovery = await created.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.True(executorAfterRecovery.Schema.QueryType.Fields.ContainsName("accepted"));
    }

    [Fact]
    public async Task Update_Should_DisposeSchemaServices_When_ExecutorCreationFailsAfterBuildingThem()
    {
        // arrange
        var failPipeline = false;
        DisposalProbe? builtProbe = null;
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<DisposalProbe>())
                .UseRequest((context, next) =>
                {
                    if (failPipeline)
                    {
                        builtProbe = context.Schema.Services.GetRequiredService<DisposalProbe>();
                        throw new InvalidOperationException("pipeline failed");
                    }

                    return next;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        failPipeline = true;
        configProvider.UpdateConfiguration(CreateConfiguration("rejected"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await builtProbe!.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal("pipeline failed", failure.Exception.Message);
    }

    [Fact]
    public async Task Update_Should_DisposeCandidateAndRaiseNoEvent_When_ManagerIsDisposedDuringWarmup()
    {
        // arrange
        var blockWarmup = false;
        DisposalProbe? candidateProbe = null;
        var warmupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new RecordingListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<DisposalProbe>())
                .AddWarmupTask(async (executor, cancellationToken) =>
                {
                    if (blockWarmup)
                    {
                        candidateProbe = executor.Schema.Services.GetRequiredService<DisposalProbe>();
                        warmupStarted.TrySetResult();
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        listener.Events.Clear();

        // act
        blockWarmup = true;
        configProvider.UpdateConfiguration(CreateConfiguration("candidate"));
        await warmupStarted.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await manager.DisposeAsync();
        await candidateProbe!.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Empty(listener.Events);
    }

    [Fact]
    public async Task Update_Should_KeepNewExecutorActiveAndLoopAlive_When_ExecutorCreatedListenerThrows()
    {
        // arrange
        var listener = new PostSwapListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var swapped = ObserveCreatedExecutors(manager);
        listener.ThrowOnCreated = true;

        // act
        configProvider.UpdateConfiguration(CreateConfiguration("swapped"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterSwap = await swapped.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var activeExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        listener.ThrowOnCreated = false;
        var recovered = ObserveCreatedExecutors(manager);
        configProvider.UpdateConfiguration(CreateConfiguration("recovered"));
        var executorAfterRecovery = await recovered.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal("created failed", failure.Exception.Message);
        Assert.True(executorAfterSwap.Schema.QueryType.Fields.ContainsName("swapped"));
        Assert.Same(executorAfterSwap, activeExecutor);
        Assert.True(executorAfterRecovery.Schema.QueryType.Fields.ContainsName("recovered"));
    }

    [Fact]
    public async Task Update_Should_NotifyLaterObserversAndEvictPreviousExecutor_When_EventObserverThrows()
    {
        // arrange
        var listener = new PostSwapListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        var initialExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        manager.Subscribe(new RequestExecutorEventObserver(@event =>
        {
            if (@event.Type == RequestExecutorEventType.Created)
            {
                throw new InvalidOperationException("observer failed");
            }
        }));
        var swapped = ObserveCreatedExecutors(manager);

        // act
        configProvider.UpdateConfiguration(CreateConfiguration("swapped"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var evicted = await listener.Evicted.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterSwap = await swapped.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var activeExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal("observer failed", failure.Exception.Message);
        Assert.Same(executorAfterSwap, failure.Executor);
        Assert.Same(initialExecutor, evicted);
        Assert.Same(activeExecutor, executorAfterSwap);
        Assert.True(activeExecutor.Schema.QueryType.Fields.ContainsName("swapped"));
    }

    [Fact]
    public async Task Update_Should_ReportCleanupFailureAndDisposePreviousExecutor_When_EvictedListenerThrows()
    {
        // arrange
        var listener = new PostSwapListener { ThrowOnEvicted = true };
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<DisposalProbe>())
                .ModifyOptions(o => o.EvictionTimeout = TimeSpan.Zero)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        var initialExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var previousProbe = initialExecutor.Schema.Services.GetRequiredService<DisposalProbe>();
        var swapped = ObserveCreatedExecutors(manager);

        // act
        configProvider.UpdateConfiguration(CreateConfiguration("swapped"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterSwap = await swapped.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await previousProbe.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, failure.SchemaName);
        Assert.Equal("evicted failed", failure.Exception.Message);
        Assert.Same(executorAfterSwap, failure.Executor);
    }

    [Fact]
    public async Task Update_Should_ReportCleanupFailure_When_PreviousConfigurationDisposalThrows()
    {
        // arrange
        var listener = new PostSwapListener();
        var configProvider = new TestFusionConfigurationProvider(CreateThrowingConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var swapped = ObserveCreatedExecutors(manager);

        // act
        configProvider.UpdateConfiguration(CreateConfiguration("swapped"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterSwap = await swapped.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var activeExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.Equal("configuration disposal failed", failure.Exception.Message);
        Assert.Same(executorAfterSwap, failure.Executor);
        Assert.Same(executorAfterSwap, activeExecutor);
    }

    [Fact]
    public async Task Update_Should_ReportEveryObserverFailureAsAggregate_When_MultipleEventObserversThrow()
    {
        // arrange
        var listener = new PostSwapListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        manager.Subscribe(new RequestExecutorEventObserver(@event =>
        {
            if (@event.Type == RequestExecutorEventType.Created)
            {
                throw new InvalidOperationException("first observer failed");
            }
        }));
        manager.Subscribe(new RequestExecutorEventObserver(@event =>
        {
            if (@event.Type == RequestExecutorEventType.Created)
            {
                throw new InvalidOperationException("second observer failed");
            }
        }));
        var swapped = ObserveCreatedExecutors(manager);

        // act
        configProvider.UpdateConfiguration(CreateConfiguration("swapped"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterSwap = await swapped.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        var aggregate = Assert.IsType<AggregateException>(failure.Exception);
        Assert.Equal(
            ["first observer failed", "second observer failed"],
            aggregate.InnerExceptions.Select(e => e.Message));
        Assert.Same(executorAfterSwap, failure.Executor);
    }

    [Fact]
    public async Task Update_Should_ReportCleanupFailure_When_DelayedDisposalOfThePreviousExecutorThrows()
    {
        // arrange
        var listener = new PostSwapListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<ThrowingDisposable>())
                .ModifyOptions(o => o.EvictionTimeout = TimeSpan.Zero)
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        var initialExecutor = await manager.GetExecutorAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        _ = initialExecutor.Schema.Services.GetRequiredService<ThrowingDisposable>();
        var swapped = ObserveCreatedExecutors(manager);

        // act
        configProvider.UpdateConfiguration(CreateConfiguration("swapped"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        var executorAfterSwap = await swapped.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(ISchemaDefinition.DefaultName, failure.SchemaName);
        Assert.Equal("disposal failed", failure.Exception.Message);
        Assert.Same(executorAfterSwap, failure.Executor);
    }

    [Fact]
    public async Task GetExecutor_Should_ThrowBothFailures_When_FirstCreationDisposalThrows()
    {
        // arrange
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<ThrowingDisposable>())
                .UseRequest((context, next) =>
                {
                    _ = context.Schema.Services.GetRequiredService<ThrowingDisposable>();
                    throw new InvalidOperationException("pipeline failed");
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();

        // act
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken));

        // assert
        Assert.Equal(
            ["pipeline failed", "disposal failed"],
            exception.InnerExceptions.Select(e => e.Message));
    }

    [Fact]
    public async Task Update_Should_ReportDisposalFailureAndSurfaceCreationException_When_DisposingSchemaServicesThrows()
    {
        // arrange
        var failPipeline = false;
        var listener = new FailureCollectingListener(expectedCount: 2);
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ => listener)
                .ConfigureSchemaServices((_, s) => s.AddSingleton<ThrowingDisposable>())
                .UseRequest((context, next) =>
                {
                    if (failPipeline)
                    {
                        _ = context.Schema.Services.GetRequiredService<ThrowingDisposable>();
                        throw new InvalidOperationException("pipeline failed");
                    }

                    return next;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        failPipeline = true;
        configProvider.UpdateConfiguration(CreateConfiguration("rejected"));
        var failures = await listener.Completed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(["disposal failed", "pipeline failed"], failures);
    }

    [Fact]
    public async Task Update_Should_DisposeCandidateSchemaServices_When_DiagnosticListenerResolutionThrows()
    {
        // arrange
        var failPipeline = false;
        DisposalProbe? candidateProbe = null;
        var listener = new UpdateFailureListener();
        var configProvider = new TestFusionConfigurationProvider(CreateConfiguration("field"));

        var services =
            new ServiceCollection()
                .AddGraphQLGateway()
                .AddConfigurationProvider(_ => configProvider)
                .AddDiagnosticEventListener(_ =>
                {
                    return failPipeline
                        ? throw new InvalidOperationException("listener resolution failed")
                        : listener;
                })
                .ConfigureSchemaServices((_, s) => s.AddSingleton<DisposalProbe>())
                .UseRequest((context, next) =>
                {
                    if (failPipeline)
                    {
                        candidateProbe = context.Schema.Services.GetRequiredService<DisposalProbe>();
                        throw new InvalidOperationException("pipeline failed");
                    }

                    return next;
                })
                .Services
                .BuildServiceProvider();

        var manager = services.GetRequiredService<FusionRequestExecutorManager>();
        await manager.GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        failPipeline = true;
        configProvider.UpdateConfiguration(CreateConfiguration("rejected"));
        var failure = await listener.Failure.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await candidateProbe!.Disposed.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal("pipeline failed", failure.Exception.Message);
    }

    private static Task<IRequestExecutor> ObserveCreatedExecutors(FusionRequestExecutorManager manager)
    {
        var created = new TaskCompletionSource<IRequestExecutor>(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.Subscribe(new RequestExecutorEventObserver(@event =>
        {
            if (@event.Type == RequestExecutorEventType.Created)
            {
                created.TrySetResult(@event.Executor);
            }
        }));

        return created.Task;
    }

    private static FusionConfiguration CreateConfiguration(string fieldName)
        => CreateFusionConfiguration(
            $$"""
            type Query {
              {{fieldName}}: String!
            }
            """);

    private static FusionConfiguration CreateThrowingConfiguration(string fieldName)
        => new(
            CreateConfiguration(fieldName).Schema,
            new JsonDocumentOwner(JsonDocument.Parse("{ }"), new ThrowingMemoryOwner()));

    private sealed class ThrowingMemoryOwner : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("configuration disposal failed");
    }

    private sealed class UpdateFailureListener : FusionExecutionDiagnosticEventListener
    {
        private readonly TaskCompletionSource<(string SchemaName, Exception Exception)> _failure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(string SchemaName, Exception Exception)> Failure => _failure.Task;

        public override void ExecutorUpdateFailed(string schemaName, Exception exception)
            => _failure.TrySetResult((schemaName, exception));
    }

    private sealed class DisposalProbe : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public void Dispose() => _disposed.TrySetResult();
    }

    private sealed class ThrowingUpdateFailureListener : FusionExecutionDiagnosticEventListener
    {
        private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Invoked => _invoked.Task;

        public override void ExecutorUpdateFailed(string schemaName, Exception exception)
        {
            _invoked.TrySetResult();
            throw new InvalidOperationException("listener failed");
        }
    }

    private sealed class PostSwapListener : FusionExecutionDiagnosticEventListener
    {
        private readonly TaskCompletionSource<(string SchemaName, IRequestExecutor Executor, Exception Exception)>
            _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IRequestExecutor> _evicted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile bool _throwOnCreated;
        private volatile bool _throwOnEvicted;

        public bool ThrowOnCreated
        {
            get => _throwOnCreated;
            set => _throwOnCreated = value;
        }

        public bool ThrowOnEvicted
        {
            get => _throwOnEvicted;
            set => _throwOnEvicted = value;
        }

        public Task<(string SchemaName, IRequestExecutor Executor, Exception Exception)> Failure => _failure.Task;

        public Task<IRequestExecutor> Evicted => _evicted.Task;

        public override void ExecutorCreated(string name, IRequestExecutor executor)
        {
            if (ThrowOnCreated)
            {
                throw new InvalidOperationException("created failed");
            }
        }

        public override void ExecutorEvicted(string name, IRequestExecutor executor)
        {
            _evicted.TrySetResult(executor);

            if (ThrowOnEvicted)
            {
                throw new InvalidOperationException("evicted failed");
            }
        }

        public override void ExecutorUpdateCleanupFailed(
            string schemaName,
            IRequestExecutor executor,
            Exception exception)
            => _failure.TrySetResult((schemaName, executor, exception));
    }

    private sealed class FailureCollectingListener(int expectedCount) : FusionExecutionDiagnosticEventListener
    {
        private readonly ConcurrentQueue<string> _messages = [];
        private readonly TaskCompletionSource<string[]> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string[]> Completed => _completed.Task;

        public override void ExecutorUpdateFailed(string schemaName, Exception exception)
        {
            _messages.Enqueue(exception.Message);

            if (_messages.Count >= expectedCount)
            {
                _completed.TrySetResult([.. _messages]);
            }
        }
    }

    private sealed class RecordingListener : FusionExecutionDiagnosticEventListener
    {
        public ConcurrentQueue<string> Events { get; } = [];

        public override void ExecutorCreated(string name, IRequestExecutor executor)
            => Events.Enqueue($"created:{name}");

        public override void ExecutorEvicted(string name, IRequestExecutor executor)
            => Events.Enqueue($"evicted:{name}");

        public override void ExecutorUpdateFailed(string schemaName, Exception exception)
            => Events.Enqueue($"failed:{schemaName}");
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public void Dispose()
        {
            _disposed.TrySetResult();
            throw new InvalidOperationException("disposal failed");
        }
    }
}
