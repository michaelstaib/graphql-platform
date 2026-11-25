using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.Pipeline;

public sealed class SubscriptionIncrementalDirectiveTests
{
    [Fact]
    public async Task Subscription_Should_ReturnError_When_DeferIsEnabledByVariable()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($defer: Boolean!) {
                  onMessage {
                    ... @defer(if: $defer) {
                      body
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["defer"] = true })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResult().MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The @defer directive cannot be enabled in a subscription operation. Pass false to its `if` argument to disable it.",
                  "extensions": {
                    "directive": "defer"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_ReturnError_When_StreamIsEnabledByVariable()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($stream: Boolean!) {
                  onMessage {
                    tags @stream(if: $stream)
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["stream"] = true })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResult().MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The @stream directive cannot be enabled in a subscription operation. Pass false to its `if` argument to disable it.",
                  "extensions": {
                    "directive": "stream"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_ReturnError_When_DeferIfVariableDefaultsToTrue()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($defer: Boolean! = true) {
                  onMessage {
                    ... @defer(if: $defer) {
                      body
                    }
                  }
                }
                """)
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResult().MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The @defer directive cannot be enabled in a subscription operation. Pass false to its `if` argument to disable it.",
                  "extensions": {
                    "directive": "defer"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_DeferIsDisabledByVariable()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($defer: Boolean!) {
                  onMessage {
                    ... @defer(if: $defer) {
                      body
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["defer"] = false })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "body": "hello"
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_StreamIsDisabledByVariable()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($stream: Boolean!) {
                  onMessage {
                    tags @stream(if: $stream)
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["stream"] = false })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "tags": [
                    "a",
                    "b"
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_EnabledDeferIsSkippedByVariable()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($skip: Boolean!) {
                  onMessage {
                    id
                    ... @skip(if: $skip) @defer {
                      body
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["skip"] = true })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "id": 1
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_ReturnError_When_DeferInNamedFragmentIsEnabledByVariable()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($d: Boolean!) {
                  onMessage {
                    ...Body
                  }
                }

                fragment Body on Message {
                  ... @defer(if: $d) {
                    body
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["d"] = true })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResult().MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The @defer directive cannot be enabled in a subscription operation. Pass false to its `if` argument to disable it.",
                  "extensions": {
                    "directive": "defer"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_DeferInNamedFragmentIsDisabledByVariable()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($d: Boolean!) {
                  onMessage {
                    ...Body
                  }
                }

                fragment Body on Message {
                  ... @defer(if: $d) {
                    body
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["d"] = false })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "body": "hello"
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_ReturnError_When_StreamInNamedFragmentIsEnabledByVariable()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($s: Boolean!) {
                  onMessage {
                    ...Tags
                  }
                }

                fragment Tags on Message {
                  tags @stream(if: $s)
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["s"] = true })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResult().MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The @stream directive cannot be enabled in a subscription operation. Pass false to its `if` argument to disable it.",
                  "extensions": {
                    "directive": "stream"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_StreamInNamedFragmentIsDisabledByVariable()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($s: Boolean!) {
                  onMessage {
                    ...Tags
                  }
                }

                fragment Tags on Message {
                  tags @stream(if: $s)
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["s"] = false })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "tags": [
                    "a",
                    "b"
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_EnabledDeferDefersNothingAsItIsMergedIntoSibling()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($defer: Boolean!) {
                  onMessage {
                    body
                    ... @defer(if: $defer) {
                      body
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["defer"] = true })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "body": "hello"
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_ReturnError_When_EnabledOuterDeferStillDefersAsNestedDeferIsDisabled()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($outer: Boolean!, $inner: Boolean!) {
                  onMessage {
                    ... @defer(if: $outer) {
                      ... @defer(if: $inner) {
                        body
                      }
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["outer"] = true, ["inner"] = false })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        result.ExpectOperationResult().MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The @defer directive cannot be enabled in a subscription operation. Pass false to its `if` argument to disable it.",
                  "extensions": {
                    "directive": "defer"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_EnabledDeferIsUnderFieldSkippedByLiteral()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($defer: Boolean!) {
                  onMessage {
                    id
                    author @skip(if: true) {
                      ... @defer(if: $defer) {
                        name
                      }
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["defer"] = true })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "id": 1
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Subscription_Should_DeliverEvents_When_EnabledDeferIsUnderFieldSkippedByVariable()
    {
        // arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var executor = await CreateExecutorAsync();
        var sender = executor.Schema.Services.GetRootServiceProvider()
            .GetRequiredService<ITopicEventSender>();

        var request = OperationRequestBuilder.New()
            .SetDocument(
                """
                subscription($defer: Boolean!, $skip: Boolean!) {
                  onMessage {
                    id
                    author @skip(if: $skip) {
                      ... @defer(if: $defer) {
                        name
                      }
                    }
                  }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["defer"] = true, ["skip"] = true })
            .Build();

        var subscribe = await executor.ExecuteAsync(request, cts.Token);
        await using var stream = subscribe.ExpectResponseStream();
        await using var enumerator = stream.ReadResultsAsync().GetAsyncEnumerator(cts.Token);

        // act
        await sender.SendAsync("OnMessage", new Message("hello", ["a", "b"]), cts.Token);
        var moved = await enumerator.MoveNextAsync();

        // assert
        Assert.True(moved);
        enumerator.Current.MatchInlineSnapshot(
            """
            {
              "data": {
                "onMessage": {
                  "id": 1
                }
              }
            }
            """);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddInMemorySubscriptions()
            .AddQueryType(d => d.Field("foo").Resolve("bar"))
            .AddSubscriptionType<Subscription>()
            .ModifyOptions(
                o =>
                {
                    o.EnableDefer = true;
                    o.EnableStream = true;
                })
            .Services
            .BuildServiceProvider()
            .GetRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    public sealed record Message(string Body, string[] Tags)
    {
        public int Id => 1;

        public Author Author => new("jane");
    }

    public sealed record Author(string Name);

    public sealed class Subscription
    {
        [Subscribe]
        public Message OnMessage([EventMessage] Message message) => message;
    }
}
