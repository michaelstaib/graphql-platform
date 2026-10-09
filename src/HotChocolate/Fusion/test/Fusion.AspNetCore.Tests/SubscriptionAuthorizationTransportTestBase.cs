using System.Globalization;
using System.Security.Claims;
using HotChocolate.Fusion.Authorization;
using HotChocolate.Fusion.Authorization.Audit;
using HotChocolate.Fusion.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HotChocolate.Fusion;

/// <summary>
/// Builds a gateway in front of the <see cref="SubscriptionAuthorizationSource"/> whose principal
/// comes from the test headers.
/// </summary>
public abstract class SubscriptionAuthorizationTransportTestBase : FusionTestBase
{
    public const string UserHeader = "X-Test-User";
    public const string ExpiryHeader = "X-Test-Expiry";
    protected const string Member = "member";
    protected const string Reader = "reader";

    protected const string ChangedSubscription = "subscription { changed { id name tag } }";

    protected static readonly SubscriptionAuthorizationSource.Item Event =
        new("1", "name", "code", "note", "tag");

    protected Task<SubscriptionGateway> CreateSubscriptionGatewayAsync(DenyHandling denyHandling)
        => CreateSubscriptionGatewayAsync(denyHandling, _ => { });

    protected async Task<SubscriptionGateway> CreateSubscriptionGatewayAsync(
        DenyHandling denyHandling,
        Action<IFusionGatewayBuilder> configure)
    {
        var feed = new SubscriptionAuthorizationFeed();
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddHours(1));
        var live = new SwitchablePolicy(SubscriptionAuthorizationSource.LivePolicy, reevaluatesPerEvent: true);
        var flip = new SwitchablePolicy(SubscriptionAuthorizationSource.FlipPolicy, reevaluatesPerEvent: true);
        var audit = new RecordingAuditProvider();

        var source = CreateSourceSchema(
            "A",
            SubscriptionAuthorizationSource.Configure,
            configureServices: services => services.AddSingleton(feed));

        var gateway = await CreateCompositeSchemaAsync(
            [("A", source)],
            configureServices: services => services.AddAuthentication().AddJwtBearer("Bearer", _ => { }),
            configureApplication: UseTestUser,
            configureGatewayBuilder: builder =>
            {
                builder.ConfigureSchemaServices(
                    (_, services) =>
                    {
                        services.AddSingleton<TimeProvider>(time);
                        services.AddSingleton<IAuditProvider>(audit);
                        services.AddSingleton<IPolicyProvider>(live);
                        services.AddSingleton<IPolicyProvider>(flip);
                    });
                builder.ModifyAuthorizationOptions(o => o.DenyHandling = denyHandling);
                configure(builder);
            },
            includeOperationPlan: false);

        return new SubscriptionGateway(gateway, feed, time, live, flip, audit);
    }

    /// <summary>
    /// Starts a subscription over server-sent events and returns once the gateway opened the
    /// source schema subscription.
    /// </summary>
    protected static async Task<SubscriptionSseClient> StartSseAsync(
        SubscriptionGateway gateway,
        HttpClient client,
        string query,
        string? user,
        string? expiry)
    {
        var expectedOpened = gateway.Feed.Opened + 1;
        var pending = SubscriptionSseClient.StartAsync(client, query, user, expiry);
        await gateway.Feed.WaitForOpenedAsync(expectedOpened);

        return await pending;
    }

    protected static string FormatExpiry(DateTimeOffset expiry)
        => expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static void UseTestUser(IApplicationBuilder app)
    {
        app.UseWebSockets();
        app.UseRouting();
        app.Use(
            async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue(UserHeader, out var user))
                {
                    var claims = new List<Claim>();

                    if (user == Reader)
                    {
                        claims.Add(new Claim("scope", "read"));
                    }

                    if (context.Request.Headers.TryGetValue(ExpiryHeader, out var expiry))
                    {
                        claims.Add(new Claim("exp", expiry.ToString()));
                    }

                    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
                }

                await next(context);
            });
        app.UseEndpoints(endpoint => endpoint.MapGraphQL());
    }

    protected sealed class SubscriptionGateway(
        Gateway gateway,
        SubscriptionAuthorizationFeed feed,
        FakeTimeProvider time,
        SwitchablePolicy live,
        SwitchablePolicy flip,
        RecordingAuditProvider audit) : IDisposable
    {
        public Gateway Gateway { get; } = gateway;

        public SubscriptionAuthorizationFeed Feed { get; } = feed;

        public FakeTimeProvider Time { get; } = time;

        public SwitchablePolicy Live { get; } = live;

        public SwitchablePolicy Flip { get; } = flip;

        public RecordingAuditProvider Audit { get; } = audit;

        public void Dispose() => Gateway.Dispose();
    }
}
