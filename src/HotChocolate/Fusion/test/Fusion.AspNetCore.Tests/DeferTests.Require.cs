using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

public partial class DeferTests
{
    [Fact]
    public async Task Defer_Should_DeliverDeferredField_When_DeferredFieldHasNoRequirement()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id soft } }",
            "{ user(id: 1) { id ... @defer { soft } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "soft": "soft-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferredFieldRequiresFieldOfOtherSubgraph()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "{ user(id: 1) { id ... @defer { total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferredGroupSelectsFieldOfProviderSubgraph()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id soft total } }",
            "{ user(id: 1) { id ... @defer { soft total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "soft": "soft-1",
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferredGroupSelectsRequiredField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id sku total } }",
            "{ user(id: 1) { id ... @defer { sku total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "sku": "sku-1",
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_RequiredFieldIsSelectedOutsideDefer()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id sku total } }",
            "{ user(id: 1) { id sku ... @defer { total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1,
                  "sku": "sku-1"
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_NestedDeferIsInsideGroupSelectingProviderField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id soft total } }",
            "{ user(id: 1) { id ... @defer { soft ... @defer { total } } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "soft": "soft-1",
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_NestedDeferIsInsideEmptyGroup()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "{ user(id: 1) { id ... @defer { ... @defer { total } } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_ProviderFieldIsInNestedDefer()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total soft } }",
            "{ user(id: 1) { id ... @defer { total ... @defer { soft } } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "total": "total-sku-1",
                    "soft": "soft-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferIfIsTrue()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "{ user(id: 1) { id ... @defer(if: true) { total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "user"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_ReturnRequireFieldInline_When_DeferIfIsFalse()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "{ user(id: 1) { id ... @defer(if: false) { total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1,
                  "total": "total-sku-1"
                }
              }
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_ReturnRequireField_When_DeferIfVariableIsTrue()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "query($d: Boolean!) { user(id: 1) { id ... @defer(if: $d) { total } } }",
            new Dictionary<string, object?> { ["d"] = true });

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
    }

    [Fact]
    public async Task Defer_Should_ReturnRequireFieldInline_When_DeferIfVariableIsFalse()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "query($d: Boolean!) { user(id: 1) { id ... @defer(if: $d) { total } } }",
            new Dictionary<string, object?> { ["d"] = false });

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 1,
                  "total": "total-sku-1"
                }
              }
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferIsOnListItems()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users { id total } }",
            "{ users { id ... @defer { total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users": [
                  {
                    "id": 1
                  },
                  {
                    "id": 2
                  },
                  {
                    "id": 3
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "subPath": [
                    0
                  ],
                  "data": {
                    "total": "total-sku-1"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    1
                  ],
                  "data": {
                    "total": "total-sku-2"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    2
                  ],
                  "data": {
                    "total": "total-sku-3"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferOnListItemsSelectsProviderField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users { id soft total } }",
            "{ users { id ... @defer { soft total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users": [
                  {
                    "id": 1
                  },
                  {
                    "id": 2
                  },
                  {
                    "id": 3
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "subPath": [
                    0
                  ],
                  "data": {
                    "soft": "soft-1",
                    "total": "total-sku-1"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    1
                  ],
                  "data": {
                    "soft": "soft-2",
                    "total": "total-sku-2"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    2
                  ],
                  "data": {
                    "soft": "soft-3",
                    "total": "total-sku-3"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferIsInMutationResult()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "mutation { createUser(id: 1) { id total } }",
            "mutation { createUser(id: 1) { id ... @defer { total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "createUser": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "createUser"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Defer_Should_DeliverRequireField_When_DeferInMutationResultSelectsProviderField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "mutation { createUser(id: 1) { id soft total } }",
            "mutation { createUser(id: 1) { id ... @defer { soft total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "createUser": {
                  "id": 1
                }
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "createUser"
                  ]
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "0",
                  "data": {
                    "soft": "soft-1",
                    "total": "total-sku-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "0"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    private static readonly JsonSerializerOptions s_indentedJson = new() { WriteIndented = true };

    private async Task<Gateway> CreateRequireGatewayAsync()
    {
        var serverA = CreateSourceSchema(
            "a",
            b => b.AddQueryType<RequireSourceA.Query>().AddMutationType<RequireSourceA.Mutation>());
        var serverB = CreateSourceSchema("b", b => b.AddQueryType<RequireSourceB.Query>());
        var serverC = CreateSourceSchema("c", b => b.AddQueryType<RequireSourceC.Query>());

        return await CreateCompositeSchemaAsync([("a", serverA), ("b", serverB), ("c", serverC)]);
    }

    private static async Task<RequireShapeRun> RunRequireShapeAsync(
        Gateway gateway,
        string plainQuery,
        string deferredQuery,
        Dictionary<string, object?>? variables = null)
    {
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        var uri = new Uri("http://localhost:5000/graphql");

        using var plainResult = await client.PostAsync(
            new OperationRequest(plainQuery),
            uri,
            TestContext.Current.CancellationToken);
        var plainData = MergeIncrementalPayloads(await ReadPayloadsAsync(plainResult));
        gateway.Interactions.Clear();

        using var deferredResult = await client.PostAsync(
            new OperationRequest(deferredQuery, variables: variables),
            uri,
            TestContext.Current.CancellationToken);
        var requestCounts = await GetRequestCountsAsync(gateway, deferredResult);
        var payloads = (await ReadPayloadsAsync(deferredResult))
            .ConvertAll(payload => JsonNode.Parse(payload)!.ToJsonString(s_indentedJson));

        return new RequireShapeRun(plainData, MergeIncrementalPayloads(payloads), payloads, requestCounts);
    }

    private sealed record RequireShapeRun(
        JsonNode PlainData,
        JsonNode DeferredData,
        List<string> Payloads,
        string RequestCounts);

    private static class RequireSourceA
    {
        public class Query
        {
            public User? GetUser(int id) => new(id);

            public List<User?> GetUsers() => [new(1), new(2), new(3)];

            [Lookup]
            [Internal]
            public User? GetUserById(int id) => new(id);
        }

        public class Mutation
        {
            public User? CreateUser(int id) => new(id);
        }

        [EntityKey("id")]
        public record User(int Id);
    }

    private static class RequireSourceB
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById(int id) => new(id);
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? Sku => "sku-" + Id;

            public string? Soft => "soft-" + Id;
        }
    }

    private static class RequireSourceC
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserById(int id) => new(id);
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? GetTotal([Require("sku")] string? sku) => "total-" + sku;
        }
    }
}
