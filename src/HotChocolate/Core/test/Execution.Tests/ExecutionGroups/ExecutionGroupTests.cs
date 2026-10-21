using System.Text.Json.Nodes;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.ExecutionGroups;

public class ExecutionGroupTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Execute_Should_RunMembersInPriorityOrder_When_MembersAreReadyOutOfOrder()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);

        // the later members are ready before the first member is released
        script.Release("connection.pageInfo.hasNextPage");
        script.Release("connection.edges");

        // act
        var execution = executor.ExecuteAsync(
            "{ connection { nodes { name } edges pageInfo { hasNextPage probe } probe } }",
            cts.Token);

        await script.Started("connection.probe");
        await script.Started("connection.pageInfo.probe");
        var eventsWhileFirstMemberRuns = script.Events;
        script.Release("connection.nodes");
        var result = await execution;

        // assert
        eventsWhileFirstMemberRuns.MatchInlineSnapshots(["start connection.nodes"]);
        script.Events.MatchInlineSnapshots(
        [
            "start connection.nodes",
            "end connection.nodes",
            "start connection.edges",
            "end connection.edges",
            "start connection.pageInfo.hasNextPage",
            "end connection.pageInfo.hasNextPage"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "connection": {
                  "nodes": [
                    {
                      "name": "a"
                    },
                    {
                      "name": "b"
                    }
                  ],
                  "edges": [
                    "e1"
                  ],
                  "pageInfo": {
                    "hasNextPage": true,
                    "probe": "probe"
                  },
                  "probe": "probe"
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_SequenceMembers_When_MembersAreSpreadAcrossInitialAndDeferredBranches()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("connection.edges");
        script.Release("connection.pageInfo.hasNextPage");

        // act
        var execution = executor.ExecuteAsync(
            """
            {
                connection {
                    nodes { name }
                    ... @defer(label: "edges") { edges edgesProbe: probe }
                    ... @defer(label: "next") { pageInfo { hasNextPage probe } }
                    probe
                }
            }
            """,
            cts.Token);

        await script.Started("connection.probe");
        await script.Started("connection.edgesProbe");
        await script.Started("connection.pageInfo.probe");
        var eventsWhileFirstMemberRuns = script.Events;
        script.Release("connection.nodes");
        var payloads = await ReadPayloadsAsync(execution, cts.Token);

        // assert
        eventsWhileFirstMemberRuns.MatchInlineSnapshots(["start connection.nodes"]);
        script.Events.MatchInlineSnapshots(
        [
            "start connection.nodes",
            "end connection.nodes",
            "start connection.edges",
            "end connection.edges",
            "start connection.pageInfo.hasNextPage",
            "end connection.pageInfo.hasNextPage"
        ]);
        payloads.MatchInlineSnapshots(
        [
            """{"connection":{"nodes":[{"name":"a"},{"name":"b"}],"probe":"probe"}}""",
            """{"edges":["e1"],"edgesProbe":"probe"}""",
            """{"pageInfo":{"hasNextPage":true,"probe":"probe"}}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_WaitForDeferredMember_When_InitialMemberFollowsInSequence()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("reversed.nodes");
        script.Release("reversed.edges");

        // act
        var execution = executor.ExecuteAsync(
            """
            {
                reversed {
                    nodes { name }
                    edges
                    ... @defer { pageInfo { hasNextPage probe } }
                    probe
                }
            }
            """,
            cts.Token);

        await script.Started("reversed.probe");
        await script.Started("reversed.pageInfo.probe");
        var eventsWhileFirstMemberRuns = script.Events;
        script.Release("reversed.pageInfo.hasNextPage");
        var payloads = await ReadPayloadsAsync(execution, cts.Token);

        // assert
        eventsWhileFirstMemberRuns.MatchInlineSnapshots(["start reversed.pageInfo.hasNextPage"]);
        script.Events.MatchInlineSnapshots(
        [
            "start reversed.pageInfo.hasNextPage",
            "end reversed.pageInfo.hasNextPage",
            "start reversed.nodes",
            "end reversed.nodes",
            "start reversed.edges",
            "end reversed.edges"
        ]);
        payloads.MatchInlineSnapshots(
        [
            """{"reversed":{"nodes":[{"name":"a"},{"name":"b"}],"edges":["e1"],"probe":"probe"}}""",
            """{"pageInfo":{"hasNextPage":true,"probe":"probe"}}"""
        ]);
    }

    [Fact]
    public async Task Execute_Should_RunNestedInstancesInParallel_When_EachResultOpensItsOwnGroup()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("connection.nodes");
        script.Release("connection.nodes[0].children.edges");
        script.Release("connection.nodes[1].children.edges");

        // act
        var execution = executor.ExecuteAsync(
            "{ connection { nodes { name children { nodes { name } edges probe } } } }",
            cts.Token);

        await script.Started("connection.nodes[0].children.nodes");
        await script.Started("connection.nodes[1].children.nodes");
        await script.Started("connection.nodes[0].children.probe");
        await script.Started("connection.nodes[1].children.probe");
        var eventsWhileInnerFirstMembersRun = script.Events.Order().ToArray();
        script.Release("connection.nodes[0].children.nodes");
        script.Release("connection.nodes[1].children.nodes");
        await execution;

        // assert
        eventsWhileInnerFirstMembersRun.MatchInlineSnapshots(
        [
            "end connection.nodes",
            "start connection.nodes",
            "start connection.nodes[0].children.nodes",
            "start connection.nodes[1].children.nodes"
        ]);
        script.Events.Where(e => e.Contains("nodes[0]")).MatchInlineSnapshots(
        [
            "start connection.nodes[0].children.nodes",
            "end connection.nodes[0].children.nodes",
            "start connection.nodes[0].children.edges",
            "end connection.nodes[0].children.edges"
        ]);
        script.Events.Where(e => e.Contains("nodes[1]")).MatchInlineSnapshots(
        [
            "start connection.nodes[1].children.nodes",
            "end connection.nodes[1].children.nodes",
            "start connection.nodes[1].children.edges",
            "end connection.nodes[1].children.edges"
        ]);
    }

    [Fact]
    public async Task Execute_Should_DropMemberFromSequence_When_MemberIsSkipped()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("connection.edges");
        script.Release("connection.pageInfo.hasNextPage");
        var request = OperationRequestBuilder
            .New()
            .SetDocument(
                """
                query ($skip: Boolean!) {
                    connection {
                        nodes @skip(if: $skip) { name }
                        edges
                        pageInfo { hasNextPage }
                    }
                }
                """)
            .SetVariableValues(new Dictionary<string, object?> { ["skip"] = true })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start connection.edges",
            "end connection.edges",
            "start connection.pageInfo.hasNextPage",
            "end connection.pageInfo.hasNextPage"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "connection": {
                  "edges": [
                    "e1"
                  ],
                  "pageInfo": {
                    "hasNextPage": true
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_ContinueSequence_When_PredecessorFails()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Fail("connection.nodes");
        script.Release("connection.edges");
        script.Release("connection.pageInfo.hasNextPage");

        // act
        var result = await executor.ExecuteAsync(
            "{ connection { nodes { name } edges pageInfo { hasNextPage } } }",
            cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start connection.nodes",
            "fail connection.nodes",
            "start connection.edges",
            "end connection.edges",
            "start connection.pageInfo.hasNextPage",
            "end connection.pageInfo.hasNextPage"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Unexpected Execution Error",
                  "path": [
                    "connection",
                    "nodes"
                  ]
                }
              ],
              "data": {
                "connection": null
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_ContinueSequence_When_PredecessorIsCancelled()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Cancel("connection.nodes");
        script.Release("connection.edges");
        script.Release("connection.pageInfo.hasNextPage");

        // act
        var result = await executor.ExecuteAsync(
            "{ connection { nodes { name } edges pageInfo { hasNextPage } } }",
            cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start connection.nodes",
            "cancel connection.nodes",
            "start connection.edges",
            "end connection.edges",
            "start connection.pageInfo.hasNextPage",
            "end connection.pageInfo.hasNextPage"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Unexpected Execution Error",
                  "path": [
                    "connection",
                    "nodes"
                  ]
                }
              ],
              "data": {
                "connection": null
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_ReleaseMembersBelowSelection_When_SelectionResolvesToNull()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.ReturnNull("reversed.pageInfo");
        script.Release("reversed.nodes");
        script.Release("reversed.edges");

        // act
        var result = await executor.ExecuteAsync(
            "{ reversed { nodes { name } edges pageInfo { hasNextPage } } }",
            cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start reversed.nodes",
            "end reversed.nodes",
            "start reversed.edges",
            "end reversed.edges"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "reversed": {
                  "nodes": [
                    {
                      "name": "a"
                    },
                    {
                      "name": "b"
                    }
                  ],
                  "edges": [
                    "e1"
                  ],
                  "pageInfo": null
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_ReleaseMembersBelowSelection_When_NonNullSelectionIsNulled()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.ReturnNull("strict.pageInfo");
        script.Release("strict.nodes");

        // act
        var result = await executor.ExecuteAsync(
            "{ strict { nodes { name } pageInfo { hasNextPage } } }",
            cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start strict.nodes",
            "end strict.nodes"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "strict",
                    "pageInfo"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "strict": null
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_WakeWaiters_When_RequestIsAborted()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);

        // act
        var execution = executor.ExecuteAsync(
            "{ connection { nodes { name } edges pageInfo { hasNextPage probe } probe } }",
            abort.Token);

        await script.Started("connection.probe");
        await script.Started("connection.pageInfo.probe");
        await abort.CancelAsync();
        var result = await execution;

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start connection.nodes",
            "cancel connection.nodes"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The GraphQL request execution was canceled.",
                  "extensions": {
                    "code": "HC0049"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_OrderAliasedMembersBySelectionOrder_When_MembersShareAPriority()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("connection.first");
        script.Release("connection.second");
        script.Release("connection.edges");

        // act
        var result = await executor.ExecuteAsync(
            """
            {
                connection {
                    ...Connection
                    second: nodes { name }
                }
            }

            fragment Connection on Connection {
                first: nodes { name }
                edges
            }
            """,
            cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start connection.first",
            "end connection.first",
            "start connection.second",
            "end connection.second",
            "start connection.edges",
            "end connection.edges"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "connection": {
                  "first": [
                    {
                      "name": "a"
                    },
                    {
                      "name": "b"
                    }
                  ],
                  "edges": [
                    "e1"
                  ],
                  "second": [
                    {
                      "name": "a"
                    },
                    {
                      "name": "b"
                    }
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_RunPureMembersInSequence_When_PureMemberIsSelectedFirst()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(descriptor =>
            {
                descriptor.Name("Query");
                descriptor
                    .Field("conn")
                    .Type<ObjectType<PureConnection>>()
                    .Resolve(_ => new PureConnection(script))
                    .ExecutionGroup(
                        new ExecutionGroupEntry("nodes", 0, isStreamable: false),
                        new ExecutionGroupEntry("pureCount", 1, isStreamable: false));
            })
            .AddObjectType<PureConnection>(descriptor =>
            {
                descriptor.Name("PureConnection");
                descriptor
                    .Field("nodes")
                    .Type<ListType<StringType>>()
                    .Resolve(ctx => script.RunAsync(ctx, new[] { "n" }));
                descriptor.Field(c => c.PureCount);
            })
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("conn.nodes");

        // act
        var result = await executor.ExecuteAsync("{ conn { pureCount nodes } }", cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start conn.nodes",
            "end conn.nodes",
            "pure pureCount"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "conn": {
                  "pureCount": true,
                  "nodes": [
                    "n"
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Execute_Should_SequenceMembersPerConcreteResult_When_GroupRootIsAnInterface()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("pages[0].nodes");
        script.Release("pages[1].nodes");

        // act
        var execution = executor.ExecuteAsync(
            "{ pages { nodes { name } pageInfo { hasNextPage probe } } }",
            cts.Token);

        await script.Started("pages[0].pageInfo.probe");
        await script.Started("pages[1].pageInfo.probe");
        var eventsWhileFirstMembersRun = script.Events.Order().ToArray();
        script.Release("pages[0].pageInfo.hasNextPage");
        script.Release("pages[1].pageInfo.hasNextPage");
        await execution;

        // assert
        eventsWhileFirstMembersRun.MatchInlineSnapshots(
        [
            "start pages[0].pageInfo.hasNextPage",
            "start pages[1].pageInfo.hasNextPage"
        ]);
        script.Events.OrderBy(e => e.Contains("pages[0]") ? 0 : 1).MatchInlineSnapshots(
        [
            "start pages[0].pageInfo.hasNextPage",
            "end pages[0].pageInfo.hasNextPage",
            "start pages[0].nodes",
            "end pages[0].nodes",
            "start pages[1].pageInfo.hasNextPage",
            "end pages[1].pageInfo.hasNextPage",
            "start pages[1].nodes",
            "end pages[1].nodes"
        ]);
    }

    [Fact]
    public async Task Execute_Should_KeepMemberTurn_When_NullableAncestorIsNulledAfterChildTasksWereCreated()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("reversed.nodes");

        // act
        var execution = executor.ExecuteAsync(
            "{ reversed { nodes { name } pageInfo { hasNextPage bad } } }",
            cts.Token);

        await script.Started("reversed.pageInfo.hasNextPage");
        await script.Started("bad");

        // the pure child has nulled pageInfo, give a wrongly released member the chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
        var eventsWhileFirstMemberRuns = script.Events;
        script.Release("reversed.pageInfo.hasNextPage");
        var result = await execution;

        // assert
        eventsWhileFirstMemberRuns.MatchInlineSnapshots(["start reversed.pageInfo.hasNextPage"]);
        script.Events.MatchInlineSnapshots(
        [
            "start reversed.pageInfo.hasNextPage",
            "end reversed.pageInfo.hasNextPage",
            "start reversed.nodes",
            "end reversed.nodes"
        ]);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "reversed",
                    "pageInfo",
                    "bad"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "reversed": {
                  "nodes": [
                    {
                      "name": "a"
                    },
                    {
                      "name": "b"
                    }
                  ],
                  "pageInfo": null
                }
              }
            }
            """);
    }

    [Fact(Skip = "A field selected in several sibling @defer fragments currently executes once per fragment.")]
    public async Task Execute_Should_RunMemberOnce_When_MemberIsSelectedInSiblingDeferFragments()
    {
        // arrange
        var script = new ExecutionScript();
        var executor = await ExecutionGroupTestSchema.CreateAsync(script);
        using var cts = new CancellationTokenSource(s_timeout);
        script.Release("connection.nodes");
        script.Release("connection.edges");

        // act
        var execution = executor.ExecuteAsync(
            """
            {
                connection {
                    nodes { name }
                    ... @defer(label: "first") { edges }
                    ... @defer(label: "second") { edges }
                }
            }
            """,
            cts.Token);

        await ReadPayloadsAsync(execution, cts.Token);

        // assert
        script.Events.MatchInlineSnapshots(
        [
            "start connection.nodes",
            "end connection.nodes",
            "start connection.edges",
            "end connection.edges"
        ]);
    }

    private static async Task<List<string>> ReadPayloadsAsync(
        Task<IExecutionResult> execution,
        CancellationToken cancellationToken)
    {
        var stream = Assert.IsType<ResponseStream>(await execution);
        var initial = new List<string>();
        var incremental = new List<string>();

        await foreach (var payload in stream.ReadResultsAsync().WithCancellation(cancellationToken))
        {
            var root = JsonNode.Parse(payload.ToJson())!;

            if (root["data"] is { } data)
            {
                initial.Add(data.ToJsonString());
            }

            if (root["incremental"] is JsonArray entries)
            {
                incremental.AddRange(entries.Select(e => e!["data"]!.ToJsonString()));
            }
        }

        incremental.Sort(StringComparer.Ordinal);
        return [.. initial, .. incremental];
    }

    internal sealed class PureConnection(ExecutionScript script)
    {
        public bool PureCount
        {
            get
            {
                script.Log("pure pureCount");
                return true;
            }
        }
    }
}
