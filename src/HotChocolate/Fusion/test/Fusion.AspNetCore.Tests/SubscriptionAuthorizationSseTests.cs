using HotChocolate.Fusion.Authorization;

namespace HotChocolate.Fusion;

public class SubscriptionAuthorizationSseTests : SubscriptionAuthorizationTransportTestBase
{
    [Fact]
    public async Task StartAsync_Should_RefuseWithUnauthorized_When_TheSubscribeFieldIsDeniedForAnonymous()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var client = gateway.Gateway.CreateClient();

        // act
        using var sse = await SubscriptionSseClient.StartAsync(client, "subscription { secret { id } }");

        // assert
        await SnapshotRefusalAsync(sse, gateway);
    }

    [Fact]
    public async Task StartAsync_Should_RefuseWithForbidden_When_TheSubscribeFieldIsDeniedForAuthenticated()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var client = gateway.Gateway.CreateClient();

        // act
        using var sse = await SubscriptionSseClient.StartAsync(
            client,
            "subscription { scoped { id } }",
            Member);

        // assert
        await SnapshotRefusalAsync(sse, gateway);
    }

    [Fact]
    public async Task StartAsync_Should_RefuseWithUnauthorized_When_TheTokenExpiredBeforeTheSubscription()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var client = gateway.Gateway.CreateClient();
        var expiredAt = FormatExpiry(gateway.Time.GetUtcNow().AddMinutes(-1));

        // act
        using var sse = await SubscriptionSseClient.StartAsync(client, ChangedSubscription, Member, expiredAt);

        // assert
        await SnapshotRefusalAsync(sse, gateway);
    }

    [Fact]
    public async Task ReadEventAsync_Should_StreamNullWithErrorOnEveryEvent_When_ANullableFieldIsDeniedAtSubscribe()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        gateway.Live.Allowed = false;
        using var client = gateway.Gateway.CreateClient();
        using var sse = await StartSseAsync(gateway, client, ChangedSubscription, Member, null);

        // act
        gateway.Feed.Publish(Event);
        var first = await sse.ReadEventAsync();
        gateway.Feed.Publish(Event);
        var second = await sse.ReadEventAsync();

        // assert
        new[] { first, second }.MatchInlineSnapshots(
            [
                """
                event: next
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
                      "tag": "tag"
                    }
                  }
                }
                """,
                """
                event: next
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
                      "tag": "tag"
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadEventAsync_Should_StreamDataNullWithErrorOnEveryEvent_When_ANonNullFieldIsDeniedAtSubscribe()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        gateway.Live.Allowed = false;
        using var client = gateway.Gateway.CreateClient();
        using var sse = await StartSseAsync(gateway, client, "subscription { required { id code } }", Member, null);

        // act
        gateway.Feed.Publish(Event);
        var first = await sse.ReadEventAsync();
        gateway.Feed.Publish(Event);
        var second = await sse.ReadEventAsync();

        // assert
        new[] { first, second }.MatchInlineSnapshots(
            [
                """
                event: next
                {
                  "errors": [
                    {
                      "message": "The current user is not authorized to access this resource.",
                      "path": [
                        "required",
                        "code"
                      ],
                      "extensions": {
                        "code": "AUTH_NOT_AUTHORIZED"
                      }
                    }
                  ],
                  "data": null
                }
                """,
                """
                event: next
                {
                  "errors": [
                    {
                      "message": "The current user is not authorized to access this resource.",
                      "path": [
                        "required",
                        "code"
                      ],
                      "extensions": {
                        "code": "AUTH_NOT_AUTHORIZED"
                      }
                    }
                  ],
                  "data": null
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadEventAsync_Should_RenderTheDenialFromTheNextEvent_When_TheVerdictFlipsToDeny()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var client = gateway.Gateway.CreateClient();
        using var sse = await StartSseAsync(gateway, client, ChangedSubscription, Member, null);

        // act
        gateway.Feed.Publish(Event);
        var first = await sse.ReadEventAsync();
        gateway.Live.Allowed = false;
        gateway.Feed.Publish(Event);
        var second = await sse.ReadEventAsync();
        gateway.Feed.Publish(Event);
        var third = await sse.ReadEventAsync();

        // assert
        new[] { first, second, third }.MatchInlineSnapshots(
            [
                """
                event: next
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "name",
                      "tag": "tag"
                    }
                  }
                }
                """,
                """
                event: next
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
                      "tag": "tag"
                    }
                  }
                }
                """,
                """
                event: next
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
                      "tag": "tag"
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadEventAsync_Should_ApplyTheAllowFlipOnlyToTheResubscribe_When_TheSubscribeDeniedTheField()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        gateway.Live.Allowed = false;
        using var client = gateway.Gateway.CreateClient();
        using var original = await StartSseAsync(gateway, client, ChangedSubscription, Member, null);
        gateway.Live.Allowed = true;
        using var resubscribed = await StartSseAsync(gateway, client, ChangedSubscription, Member, null);

        // act
        gateway.Feed.Publish(Event);
        var originalEvent = await original.ReadEventAsync();
        var resubscribedEvent = await resubscribed.ReadEventAsync();

        // assert
        new[] { originalEvent, resubscribedEvent }.MatchInlineSnapshots(
            [
                """
                event: next
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
                      "tag": "tag"
                    }
                  }
                }
                """,
                """
                event: next
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "name",
                      "tag": "tag"
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReadToEndAsync_Should_CompleteTheStream_When_TheTokenExpires()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var client = gateway.Gateway.CreateClient();
        var expiresAt = FormatExpiry(gateway.Time.GetUtcNow().AddMinutes(10));
        using var sse = await StartSseAsync(gateway, client, ChangedSubscription, Member, expiresAt);
        gateway.Feed.Publish(Event);
        var first = await sse.ReadEventAsync();

        // act
        gateway.Time.Advance(TimeSpan.FromMinutes(10));
        var remaining = await sse.ReadToEndAsync();

        // assert
        string?[] events = [first, .. remaining];
        events.MatchInlineSnapshots(
            [
                """
                event: next
                {
                  "data": {
                    "changed": {
                      "id": "1",
                      "name": "name",
                      "tag": "tag"
                    }
                  }
                }
                """,
                """
                event: complete
                """
            ]);
    }

    [Fact]
    public async Task ReadEventAsync_Should_AbortTheStream_When_APolicyFaultsMidStream()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var client = gateway.Gateway.CreateClient();
        using var sse = await StartSseAsync(gateway, client, ChangedSubscription, Member, null);
        gateway.Feed.Publish(Event);
        var first = await sse.ReadEventAsync();
        gateway.Live.Fault = new InvalidOperationException("boom");

        // act
        gateway.Feed.Publish(Event);
        await Assert.ThrowsAsync<IOException>(sse.ReadEventAsync);

        // assert
        first.MatchInlineSnapshot(
            """
            event: next
            {
              "data": {
                "changed": {
                  "id": "1",
                  "name": "name",
                  "tag": "tag"
                }
              }
            }
            """);
    }

    [Fact]
    public async Task ReadEventAsync_Should_RecordThatTheSubscribeDecisionStands_When_AnotherVerdictChanges()
    {
        // arrange
        const string subscription = "subscription { changed { id name note } }";
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        gateway.Live.Allowed = false;
        using var client = gateway.Gateway.CreateClient();
        using var sse = await StartSseAsync(gateway, client, subscription, Member, null);

        // act
        gateway.Live.Allowed = true;
        gateway.Flip.Allowed = false;
        gateway.Feed.Publish(Event);
        var changed = await sse.ReadEventAsync();

        // assert
        Snapshot.Create()
            .Add(changed, "Event")
            .Add(gateway.Audit.Scopes.ToArray(), "Scopes")
            .MatchMarkdownSnapshot();
    }

    private static async Task SnapshotRefusalAsync(SubscriptionSseClient sse, SubscriptionGateway gateway)
    {
        Snapshot.Create()
            .Add($"{(int)sse.StatusCode} {sse.StatusCode}", "Status")
            .Add(sse.Challenge ?? "none", "Challenge")
            .Add((await sse.ReadToEndAsync()).ToArray(), "Events")
            .Add(gateway.Feed.Opened, "Source Schema Subscriptions")
            .Add(gateway.Audit.Scopes.ToArray(), "Scopes")
            .MatchMarkdownSnapshot();
    }
}
