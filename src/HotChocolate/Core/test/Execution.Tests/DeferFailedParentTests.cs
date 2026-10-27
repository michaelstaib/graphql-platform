using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class DeferFailedParentTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Defer_Should_NotAnnounceNestedFragment_When_ParentFragmentHasNoData()
    {
        // arrange
        var executor = await CreateExecutorAsync(Task.CompletedTask, Task.CompletedTask, new TaskCompletionSource());

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            """
            {
              root
              ... @defer(label: "P") {
                bad
                obj { y ... @defer(label: "C") { z } }
              }
            }
            """);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "root": "root"
              },
              "pending": [
                {
                  "id": "2",
                  "path": [],
                  "label": "P"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "completed": [
                {
                  "id": "2",
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "bad"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ]
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_NotAnnounceNestedFragment_When_ItsObjectWasNulledInTheParentFragment()
    {
        // arrange
        var executor = await CreateExecutorAsync(Task.CompletedTask, Task.CompletedTask, new TaskCompletionSource());

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            """
            {
              root
              ... @defer(label: "P") {
                parentData
                obj { bad ... @defer(label: "C") { z } }
              }
            }
            """);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "root": "root"
              },
              "pending": [
                {
                  "id": "2",
                  "path": [],
                  "label": "P"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "2",
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "obj",
                        "bad"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ],
                  "data": {
                    "parentData": "parentData",
                    "obj": null
                  }
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Defer_Should_NotAnnounceNestedFragment_When_ParentFragmentFailsInEitherSettlementOrder(
        bool nestedWorkSettlesFirst)
    {
        // arrange
        var nestedWorkSettled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parentDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = await CreateExecutorAsync(
            beforeBad: nestedWorkSettlesFirst ? nestedWorkSettled.Task : Task.CompletedTask,
            beforeZ: nestedWorkSettlesFirst ? Task.CompletedTask : parentDelivered.Task,
            nestedWorkSettled);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            """
            {
              root
              ... @defer(label: "P") {
                bad
                obj { y ... @defer(label: "C") { z } }
              }
            }
            """,
            payload =>
            {
                if (payload.Contains("\"completed\"", StringComparison.Ordinal))
                {
                    parentDelivered.TrySetResult();
                }
            });

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "root": "root"
              },
              "pending": [
                {
                  "id": "2",
                  "path": [],
                  "label": "P"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "completed": [
                {
                  "id": "2",
                  "errors": [
                    {
                      "message": "Cannot return null for non-nullable field.",
                      "path": [
                        "bad"
                      ],
                      "extensions": {
                        "code": "HC0018"
                      }
                    }
                  ]
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    private static async Task<List<string>> ReadPayloadsAsync(
        IRequestExecutor executor,
        string query,
        Action<string>? onPayload = null)
    {
        return await Task.Run(
                async () =>
                {
                    var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
                    var payloads = new List<string>();

                    await foreach (var payload in Assert.IsType<ResponseStream>(result)
                        .ReadResultsAsync()
                        .WithCancellation(TestContext.Current.CancellationToken))
                    {
                        await using var cleanup = payload;
                        var json = payload.ToJson();
                        payloads.Add(json);
                        onPayload?.Invoke(json);
                    }

                    return payloads;
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync(
        Task beforeBad,
        Task beforeZ,
        TaskCompletionSource nestedWorkSettled)
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(
                descriptor =>
                {
                    descriptor.Field("root").Type<StringType>().Resolve("root");
                    descriptor.Field("parentData").Type<StringType>().Resolve("parentData");
                    descriptor
                        .Field("bad")
                        .Type<NonNullType<StringType>>()
                        .Resolve(async _ =>
                        {
                            await beforeBad.ConfigureAwait(false);
                            return null;
                        });
                    descriptor.Field("obj").Type("Obj").Resolve(new object());
                })
            .AddType(
                new ObjectType(
                    descriptor =>
                    {
                        descriptor.Name("Obj");
                        descriptor.Field("bad").Type<NonNullType<StringType>>().Resolve(_ => (object?)null);
                        descriptor.Field("y").Type<StringType>().Resolve("y");
                        descriptor
                            .Field("z")
                            .Type<StringType>()
                            .Resolve(async _ =>
                            {
                                await beforeZ.ConfigureAwait(false);
                                nestedWorkSettled.TrySetResult();
                                return "z";
                            });
                    }))
            .ModifyOptions(options => options.EnableDefer = true)
            .BuildRequestExecutorAsync();
}
