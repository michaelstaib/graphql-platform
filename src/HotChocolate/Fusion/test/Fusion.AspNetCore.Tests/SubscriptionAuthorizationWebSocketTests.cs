using System.Net.WebSockets;
using HotChocolate.Fusion.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public class SubscriptionAuthorizationWebSocketTests : SubscriptionAuthorizationTransportTestBase
{
    [Fact]
    public async Task ReceiveAsync_Should_SendTheDenialAsPerIdError_When_TheSubscribeFieldIsDeniedForAnonymous()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var socket = await ConnectAsync(gateway, user: null, expiry: null);

        // act
        await socket.SendSubscribeAsync("1", "subscription { secret { id } }");
        var denied = await socket.ReceiveAsync();
        await socket.SendSubscribeAsync("2", "subscription { changed { id tag } }");
        await gateway.Feed.WaitForOpenedAsync(1);
        gateway.Feed.Publish(Event);
        var allowed = await socket.ReceiveAsync();

        // assert
        new[] { denied, allowed }.MatchInlineSnapshots(
            [
                """
                {
                  "id": "1",
                  "type": "error",
                  "payload": [
                    {
                      "message": "The current user is not authenticated.",
                      "extensions": {
                        "code": "AUTH_NOT_AUTHENTICATED"
                      }
                    }
                  ]
                }
                """,
                """
                {
                  "id": "2",
                  "type": "next",
                  "payload": {
                    "data": {
                      "changed": {
                        "id": "1",
                        "tag": "tag"
                      }
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReceiveAsync_Should_SendTheDenialAsPerIdError_When_TheSubscribeFieldIsDeniedForAuthenticated()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var socket = await ConnectAsync(gateway, Member, expiry: null);

        // act
        await socket.SendSubscribeAsync("1", "subscription { scoped { id } }");
        var denied = await socket.ReceiveAsync();

        // assert
        denied.MatchInlineSnapshot(
            """
            {
              "id": "1",
              "type": "error",
              "payload": [
                {
                  "message": "The current user is not authorized to access this resource.",
                  "extensions": {
                    "code": "AUTH_NOT_AUTHORIZED"
                  }
                }
              ]
            }
            """);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task ReceiveAsync_Should_SendTheRefusalAsPerIdError_When_TheTokenExpiredBeforeTheSubscription()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        var expiredAt = FormatExpiry(gateway.Time.GetUtcNow().AddMinutes(-1));
        using var socket = await ConnectAsync(gateway, Member, expiredAt);

        // act
        await socket.SendSubscribeAsync("1", ChangedSubscription);
        var refused = await socket.ReceiveAsync();

        // assert
        Snapshot.Create()
            .Add(refused, "Message")
            .Add(gateway.Feed.Opened, "Source Schema Subscriptions")
            .Add(gateway.Audit.Scopes.ToArray(), "Scopes")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task ReceiveAsync_Should_SendPerEventDenialsWithoutEnding_When_TheVerdictFlipsToDeny()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var socket = await ConnectAsync(gateway, Member, expiry: null);
        await socket.SendSubscribeAsync("1", ChangedSubscription);
        await gateway.Feed.WaitForOpenedAsync(1);

        // act
        gateway.Feed.Publish(Event);
        var first = await socket.ReceiveAsync();
        gateway.Live.Allowed = false;
        gateway.Feed.Publish(Event);
        var second = await socket.ReceiveAsync();
        gateway.Feed.Publish(Event);
        var third = await socket.ReceiveAsync();

        // assert
        new[] { first, second, third }.MatchInlineSnapshots(
            [
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
                    "data": {
                      "changed": {
                        "id": "1",
                        "name": "name",
                        "tag": "tag"
                      }
                    }
                  }
                }
                """,
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
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
                }
                """,
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
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
                }
                """
            ]);
    }

    [Fact]
    public async Task ReceiveAsync_Should_ApplyTheAllowFlipOnlyToTheResubscribe_When_TheSubscribeDeniedTheField()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        gateway.Live.Allowed = false;
        using var original = await ConnectAsync(gateway, Member, expiry: null);
        await original.SendSubscribeAsync("1", ChangedSubscription);
        await gateway.Feed.WaitForOpenedAsync(1);
        gateway.Live.Allowed = true;
        using var resubscribed = await ConnectAsync(gateway, Member, expiry: null);
        await resubscribed.SendSubscribeAsync("1", ChangedSubscription);
        await gateway.Feed.WaitForOpenedAsync(2);

        // act
        gateway.Feed.Publish(Event);
        var originalMessage = await original.ReceiveAsync();
        var resubscribedMessage = await resubscribed.ReceiveAsync();

        // assert
        new[] { originalMessage, resubscribedMessage }.MatchInlineSnapshots(
            [
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
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
                }
                """,
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
                    "data": {
                      "changed": {
                        "id": "1",
                        "name": "name",
                        "tag": "tag"
                      }
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task ReceiveAsync_Should_CloseTheConnectionAsUnauthorized_When_TheTokenExpires()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        var expiresAt = FormatExpiry(gateway.Time.GetUtcNow().AddMinutes(10));
        using var socket = await ConnectAsync(gateway, Member, expiresAt);
        await socket.SendSubscribeAsync("1", ChangedSubscription);
        await gateway.Feed.WaitForOpenedAsync(1);
        gateway.Feed.Publish(Event);
        var first = await socket.ReceiveAsync();

        // act
        gateway.Time.Advance(TimeSpan.FromMinutes(10));
        var closed = await socket.ReceiveAsync();

        // assert
        new[] { first, closed }.MatchInlineSnapshots(
            [
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
                    "data": {
                      "changed": {
                        "id": "1",
                        "name": "name",
                        "tag": "tag"
                      }
                    }
                  }
                }
                """,
                """
                close 4401 Unauthorized
                """
            ]);
    }

    [Fact]
    public async Task ReceiveAsync_Should_SendPerIdErrorAndKeepTheConnection_When_APolicyFaultsMidStream()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(DenyHandling.Error);
        using var socket = await ConnectAsync(gateway, Member, expiry: null);
        await socket.SendSubscribeAsync("1", ChangedSubscription);
        await gateway.Feed.WaitForOpenedAsync(1);
        gateway.Feed.Publish(Event);
        var first = await socket.ReceiveAsync();
        gateway.Live.Fault = new InvalidOperationException("boom");

        // act
        gateway.Feed.Publish(Event);
        var fault = await socket.ReceiveAsync();
        gateway.Live.Fault = null;
        await socket.SendSubscribeAsync("2", ChangedSubscription);
        await gateway.Feed.WaitForOpenedAsync(2);
        gateway.Feed.Publish(Event);
        var next = await socket.ReceiveAsync();

        // assert
        new[] { first, fault, next }.MatchInlineSnapshots(
            [
                """
                {
                  "id": "1",
                  "type": "next",
                  "payload": {
                    "data": {
                      "changed": {
                        "id": "1",
                        "name": "name",
                        "tag": "tag"
                      }
                    }
                  }
                }
                """,
                """
                {
                  "id": "1",
                  "type": "error",
                  "payload": [
                    {
                      "message": "Unexpected Execution Error"
                    }
                  ]
                }
                """,
                """
                {
                  "id": "2",
                  "type": "next",
                  "payload": {
                    "data": {
                      "changed": {
                        "id": "1",
                        "name": "name",
                        "tag": "tag"
                      }
                    }
                  }
                }
                """
            ]);
    }

    [Fact]
    public async Task InitializeAsync_Should_AnswerAccordingToTheInterceptor_When_ConnectionInitIsSent()
    {
        // arrange
        using var gateway = await CreateSubscriptionGatewayAsync(
            DenyHandling.Error,
            builder => builder.AddSocketSessionInterceptor<RejectingSocketSessionInterceptor>());
        using var anonymous = await SubscriptionWebSocketClient.ConnectAsync(
            gateway.Gateway.CreateWebSocketClient(),
            user: null,
            expiry: null);
        using var member = await SubscriptionWebSocketClient.ConnectAsync(
            gateway.Gateway.CreateWebSocketClient(),
            Member,
            expiry: null);
        using var reader = await SubscriptionWebSocketClient.ConnectAsync(
            gateway.Gateway.CreateWebSocketClient(),
            Reader,
            expiry: null);

        // act
        var anonymousAnswer = await anonymous.InitializeAsync();
        var memberAnswer = await member.InitializeAsync();
        var readerAnswer = await reader.InitializeAsync();

        // assert
        new[] { anonymousAnswer, memberAnswer, readerAnswer }.MatchInlineSnapshots(
            [
                """
                close 4401 Unauthenticated
                """,
                """
                close 4403 Forbidden
                """,
                """
                {
                  "type": "connection_ack"
                }
                """
            ]);
    }

    private static async Task<SubscriptionWebSocketClient> ConnectAsync(
        SubscriptionGateway gateway,
        string? user,
        string? expiry)
    {
        var socket = await SubscriptionWebSocketClient.ConnectAsync(
            gateway.Gateway.CreateWebSocketClient(),
            user,
            expiry);
        await socket.InitializeAsync();

        return socket;
    }
}
