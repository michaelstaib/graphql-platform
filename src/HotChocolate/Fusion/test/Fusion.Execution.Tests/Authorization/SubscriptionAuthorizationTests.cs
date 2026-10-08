using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization.Audit;
using HotChocolate.Fusion.Authorization.InMemory;
using HotChocolate.Fusion.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HotChocolate.Fusion.Authorization;

public class SubscriptionAuthorizationTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          open: Int
        }

        type Subscription {
          changed: Item
          secret: String @authenticated
          scoped: String @requiresScopes(scopes: [["read"]])
        }

        type Item {
          id: ID!
          name: String @policy(policies: [["live"]])
          tag: String
        }
        """;

    private const string Event =
        """
        { "changed": { "id": "1", "name": "n", "tag": "t" } }
        """;

    private const string Subscription = "subscription { changed { id name tag } }";

    [Fact]
    public async Task Subscribe_Should_RefuseWithUnauthorized_When_SubscribeFieldIsDeniedForAnonymous()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var executor = await CreateSubscriptionExecutorAsync(client, new ToggledPolicy("live", false));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("subscription { secret }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        Snapshot.Create()
            .Add(result, "Response")
            .Add(DescribeTransport(result), "Transport")
            .Add(client.SubscribeCalls, "Source Schema Subscriptions")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Subscribe_Should_RefuseWithForbidden_When_SubscribeFieldIsDeniedForAuthenticated()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var executor = await CreateSubscriptionExecutorAsync(client, new ToggledPolicy("live", false));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("subscription { scoped }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        Snapshot.Create()
            .Add(result, "Response")
            .Add(DescribeTransport(result), "Transport")
            .Add(client.SubscribeCalls, "Source Schema Subscriptions")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task ReadResultsAsync_Should_KeepStartDecision_When_PolicyDoesNotReevaluate()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var policy = new ToggledPolicy("live", reevaluatesPerEvent: false);
        var executor = await CreateSubscriptionExecutorAsync(client, policy);
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, Authenticated()).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        var first = await ReadNextAsync(events);
        policy.Allowed = false;
        client.Publish(Event);
        var second = await ReadNextAsync(events);

        // assert
        new[] { first, second }.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "n",
                      "tag": "t"
                    }
                  }
                }
                """,
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "n",
                      "tag": "t"
                    }
                  }
                }
                """
            ]);
        Assert.Equal(1, policy.Evaluations);
    }

    [Fact]
    public async Task ReadResultsAsync_Should_RenderNullWithError_When_VerdictFlipsToDeny()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var policy = new ToggledPolicy("live", reevaluatesPerEvent: true);
        var executor = await CreateSubscriptionExecutorAsync(
            client,
            policy,
            builder => builder.ModifyAuthorizationOptions(options => options.DenyHandling = DenyHandling.Error));
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, Authenticated()).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        var first = await ReadNextAsync(events);
        policy.Allowed = false;
        client.Publish(Event);
        var second = await ReadNextAsync(events);
        client.Publish(Event);
        var third = await ReadNextAsync(events);

        // assert
        new[] { first, second, third }.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "n",
                      "tag": "t"
                    }
                  }
                }
                """,
                """
                {
                  "errors": [
                    {
                      "message": "The current user is not authorized to access this resource.",
                      "path": [
                        "changed",
                        "name"
                      ],
                      "extensions": {
                        "code": "AUTH_NOT_AUTHORIZED"
                      }
                    }
                  ],
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": null,
                      "tag": "t"
                    }
                  }
                }
                """,
                """
                {
                  "errors": [
                    {
                      "message": "The current user is not authorized to access this resource.",
                      "path": [
                        "changed",
                        "name"
                      ],
                      "extensions": {
                        "code": "AUTH_NOT_AUTHORIZED"
                      }
                    }
                  ],
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": null,
                      "tag": "t"
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadResultsAsync_Should_RenderSilentNull_When_VerdictFlipsToDenyInNullMode()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var policy = new ToggledPolicy("live", reevaluatesPerEvent: true);
        var executor = await CreateSubscriptionExecutorAsync(client, policy);
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, Authenticated()).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        var first = await ReadNextAsync(events);
        policy.Allowed = false;
        client.Publish(Event);
        var second = await ReadNextAsync(events);

        // assert
        new[] { first, second }.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "n",
                      "tag": "t"
                    }
                  }
                }
                """,
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": null,
                      "tag": "t"
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadResultsAsync_Should_KeepFieldNull_When_VerdictFlipsToAllowAfterTheSourceSkippedIt()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var policy = new ToggledPolicy("live", reevaluatesPerEvent: true) { Allowed = false };
        var executor = await CreateSubscriptionExecutorAsync(client, policy);
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, Authenticated()).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        var first = await ReadNextAsync(events);
        policy.Allowed = true;
        client.Publish(Event);
        var second = await ReadNextAsync(events);

        // assert
        new[] { first, second }.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": null,
                      "tag": "t"
                    }
                  }
                }
                """,
                """
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": null,
                      "tag": "t"
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadResultsAsync_Should_CommitOneScopePerVerdictChange_When_PolicyReevaluates()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var audit = new RecordingAuditProvider(client);
        var policy = new ToggledPolicy("live", reevaluatesPerEvent: true);
        var committedAtSubscribe = -1;
        var executor = await CreateSubscriptionExecutorAsync(
            client,
            policy,
            builder => builder.ConfigureSchemaServices((_, sc) => sc.AddSingleton<IAuditProvider>(audit)));
        client.Subscribing = () => committedAtSubscribe = audit.Scopes.Count(s => s.CommitCount is 1);
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, Authenticated()).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        await ReadNextAsync(events);
        client.Publish(Event);
        await ReadNextAsync(events);
        policy.Allowed = false;
        client.Publish(Event);
        await ReadNextAsync(events);
        client.Publish(Event);
        await ReadNextAsync(events);

        // assert
        Snapshot.Create()
            .Add(committedAtSubscribe, "Scopes committed when the source opened")
            .Add(audit.Scopes.Select(Format).ToArray(), "Scopes")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task ReadResultsAsync_Should_EndTheStreamAndCommitAScope_When_TheTokenExpires()
    {
        // arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddHours(1));
        var client = new AuthorizationTestClient(Event);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateSubscriptionExecutorAsync(
            client,
            new ToggledPolicy("live", reevaluatesPerEvent: false),
            builder => builder.ConfigureSchemaServices(
                (_, sc) =>
                {
                    sc.AddSingleton<TimeProvider>(time);
                    sc.AddSingleton<IAuditProvider>(audit);
                }));
        var user = Authenticated(CreateExpiryClaim(time.GetUtcNow().AddMinutes(10)));
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, user).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        var first = await ReadNextAsync(events);
        time.Advance(TimeSpan.FromMinutes(10));
        var hasNext = await events.MoveNextAsync();

        // assert
        Snapshot.Create()
            .Add(first, "Event")
            .Add(hasNext, "Has Next")
            .Add(audit.Scopes.Select(Format).ToArray(), "Scopes")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task ReadResultsAsync_Should_KeepTheStreamOpen_When_TheTokenWasRenewedBeforeItExpired()
    {
        // arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddHours(1));
        var client = new AuthorizationTestClient(Event);
        var holder = new RequestContextHolder();
        var executor = await CreateSubscriptionExecutorAsync(
            client,
            new ToggledPolicy("live", reevaluatesPerEvent: false),
            builder =>
            {
                builder.ConfigureSchemaServices((_, sc) => sc.AddSingleton<TimeProvider>(time));
                builder.UseRequest(
                    (_, next) => context =>
                    {
                        holder.Context = context;
                        return next(context);
                    },
                    before: WellKnownRequestMiddleware.OperationExecutionMiddleware,
                    allowMultiple: true);
            });
        var user = Authenticated(CreateExpiryClaim(time.GetUtcNow().AddMinutes(10)));
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, user).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);
        client.Publish(Event);
        var first = await ReadNextAsync(events);
        holder.Context!.ContextData[nameof(ClaimsPrincipal)] =
            Authenticated(CreateExpiryClaim(time.GetUtcNow().AddMinutes(30)));

        // act
        time.Advance(TimeSpan.FromMinutes(10));
        client.Publish(Event);
        var renewed = await ReadNextAsync(events);
        time.Advance(TimeSpan.FromMinutes(20));
        var hasNext = await events.MoveNextAsync();

        // assert
        Snapshot.Create()
            .Add(first, "Event before the first expiry")
            .Add(renewed, "Event after the first expiry")
            .Add(hasNext, "Has Next after the renewed expiry")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Subscribe_Should_NotEvaluatePoliciesPerEvent_When_NoPolicyReevaluatesAndThePrincipalHasNoExpiry()
    {
        // arrange
        var client = new AuthorizationTestClient(Event);
        var policy = new ToggledPolicy("live", reevaluatesPerEvent: false);
        var executor = await CreateSubscriptionExecutorAsync(client, policy);
        await using var result = await executor.ExecuteAsync(
            CreateRequest(Subscription, Authenticated()).Build(),
            TestContext.Current.CancellationToken);
        await using var events = ReadEvents(result);

        // act
        client.Publish(Event);
        await ReadNextAsync(events);
        client.Publish(Event);
        await ReadNextAsync(events);
        client.Publish(Event);
        await ReadNextAsync(events);

        // assert
        Assert.Equal(1, policy.Evaluations);
    }

    private static Task<IRequestExecutor> CreateSubscriptionExecutorAsync(
        AuthorizationTestClient client,
        ToggledPolicy policy,
        Action<IFusionGatewayBuilder>? configure = null)
        => CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            null,
            builder =>
            {
                builder.ConfigureSchemaServices((_, sc) => sc.AddSingleton<IPolicyProvider>(policy));
                configure?.Invoke(builder);
            });

    private static IAsyncEnumerator<OperationResult> ReadEvents(IExecutionResult result)
        => ((ResponseStream)result).ReadResultsAsync().GetAsyncEnumerator(TestContext.Current.CancellationToken);

    private static async Task<string> ReadNextAsync(IAsyncEnumerator<OperationResult> events)
    {
        Assert.True(await events.MoveNextAsync());

        await using var current = events.Current;

        return current.ToJson();
    }

    private static Claim CreateExpiryClaim(DateTimeOffset expiry)
        => new("exp", expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    private static object DescribeTransport(IExecutionResult result)
    {
        var contextData = result.ExpectOperationResult().ContextData;

        return new
        {
            Status = contextData.GetValueOrDefault(ExecutionContextData.HttpStatusCode)?.ToString()
        };
    }

    private static string Format(RecordingAuditScope scope)
    {
        var entries = scope.Entries.Select(
            e => $"{e.Coordinate} @{e.DirectiveName}({e.PolicyName}) {e.Outcome} {e.Reason ?? "-"}");

        return $"commits={scope.CommitCount} | {string.Join("; ", entries)}";
    }

    private sealed class RequestContextHolder
    {
        public RequestContext? Context { get; set; }
    }
}
