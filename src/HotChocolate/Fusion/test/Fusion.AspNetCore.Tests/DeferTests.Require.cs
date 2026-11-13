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

    [Fact]
    public async Task Defer_Should_DeliverHeldFieldInDeferredPayload_When_ProviderStepIsPromoted()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id soft total } }",
            "{ user(id: 1) { id ... @defer { soft total } } }");

        // assert
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
    public async Task Defer_Should_DeliverHeldKeyInDeferredPayload_When_GroupSelectsKeyAndRequiringField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 1) { id total } }",
            "{ user(id: 1) { ... @defer { id total } } }");

        // assert
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {}
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
                    "id": 1,
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
    public async Task Defer_Should_DeliverHeldFieldInDeferredPayload_When_DeferIsInMutationResult()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "mutation { createUser(id: 1) { id soft total } }",
            "mutation { createUser(id: 1) { id ... @defer { soft total } } }");

        // assert
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

    [Theory]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id soft } }",
        "mutation { createUser(id: 1) { id ... @defer { soft } } }",
        null,
        "a: 1, b: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id sku total } }",
        "mutation { createUser(id: 1) { id ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id sku total } }",
        "mutation { createUser(id: 1) { id sku ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id soft total } }",
        "mutation { createUser(id: 1) { id ... @defer { soft ... @defer { total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id total } }",
        "mutation { createUser(id: 1) { id ... @defer { ... @defer { total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id soft total } }",
        "mutation { createUser(id: 1) { id ... @defer { total ... @defer { soft } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id total } }",
        "mutation { createUser(id: 1) { id ... @defer(if: true) { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id total } }",
        "mutation { createUser(id: 1) { id ... @defer(if: false) { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        1)]
    [InlineData(
        "s1",
        "mutation { createUser(id: 1) { id total } }",
        "mutation($d: Boolean!) { createUser(id: 1) { id ... @defer(if: $d) { total } } }",
        false,
        "a: 1, b: 1, c: 1",
        1)]
    [InlineData(
        "s1",
        "mutation { createUsers { id total } }",
        "mutation { createUsers { id ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "mutation { createUsers { id soft total } }",
        "mutation { createUsers { id ... @defer { soft total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "{ user(id: 1) { soft total } }",
        "{ user(id: 1) { soft ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s1",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { ... @defer { id total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft } }",
        "{ user(id: 1) { id ... @defer { soft } } }",
        null,
        "a: 1, b: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { id ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id sku total } }",
        "{ user(id: 1) { id sku ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { id ... @defer { ... @defer { total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { id ... @defer(if: true) { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { id ... @defer(if: false) { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        1)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id total } }",
        "query($d: Boolean!) { user(id: 1) { id ... @defer(if: $d) { total } } }",
        false,
        "a: 1, b: 1, c: 1",
        1)]
    [InlineData(
        "s2",
        "{ users { id total } }",
        "{ users { id ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { id total } }",
        "mutation { createUser(id: 1) { id ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s4",
        "{ user(id: 1) { id sku total } }",
        "{ user(id: 1) { id ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s4",
        "{ user(id: 1) { sku total } }",
        "{ user(id: 1) { ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s4",
        "{ user(id: 1) { id sku total } }",
        "{ user(id: 1) { ... @defer { id sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s4",
        "{ user(id: 1) { id soft total } }",
        "{ user(id: 1) { id ... @defer { soft total } } }",
        null,
        "a: 1, b: 2, c: 1",
        2)]
    [InlineData(
        "s4",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { id ... @defer { total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s4",
        "{ user(id: 1) { id sku total } }",
        "{ user(id: 1) { id sku ... @defer { total } } }",
        null,
        "b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft total } }",
        "{ user(id: 1) { id ... @defer { soft total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id sku total } }",
        "{ user(id: 1) { id ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft total } }",
        "{ user(id: 1) { id ... @defer { soft ... @defer { total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft total } }",
        "{ user(id: 1) { id ... @defer { total ... @defer { soft } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ users { id soft total } }",
        "{ users { id ... @defer { soft total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { id soft total } }",
        "mutation { createUser(id: 1) { id ... @defer { soft total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id total } }",
        "{ user(id: 1) { ... @defer { id total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ users { id sku total } }",
        "{ users { id ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { id sku total } }",
        "mutation { createUser(id: 1) { id ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ users { id soft total } }",
        "{ users { id ... @defer { soft ... @defer { total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ users { id soft total } }",
        "{ users { id ... @defer { total ... @defer { soft } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { id soft total } }",
        "mutation { createUser(id: 1) { id ... @defer { soft ... @defer { total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { id soft total } }",
        "mutation { createUser(id: 1) { id ... @defer { total ... @defer { soft } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id sku total } }",
        "{ user(id: 1) { soft ... @defer { id sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft __typename sku total } }",
        "{ user(id: 1) { id soft ... @defer { __typename sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id __typename sku total } }",
        "{ user(id: 1) { soft ... @defer { id __typename sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft __typename sku total } }",
        "{ user(id: 1) { id soft ... @defer { id __typename sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id total } }",
        "{ user(id: 1) { soft ... @defer { id total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id sku } }",
        "{ user(id: 1) { id sku ... @defer { id } } }",
        null,
        "a: 1, b: 1",
        1)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id sku total } }",
        "{ user(id: 1) { soft ... @defer(label: \"x\") { id sku total } ... @defer(label: \"y\") { id } } }",
        null,
        "a: 1, b: 1, c: 1",
        null)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id sku total } }",
        "{ user(id: 1) { soft ... @defer { sku ... @defer { id total } } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ users { soft id sku total } }",
        "{ users { soft ... @defer { id sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id sku total } }",
        "query($d: Boolean!) { user(id: 1) { soft ... @defer(if: $d) { id sku total } } }",
        true,
        "a: 1, b: 1, c: 1",
        null)]
    [InlineData(
        "s2",
        "{ user(id: 1) { soft id sku total } }",
        "query($d: Boolean!) { user(id: 1) { soft ... @defer(if: $d) { id sku total } } }",
        false,
        "a: 1, b: 1, c: 1",
        1)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft sku total } }",
        "query($d: Boolean!) { user(id: 1) { id soft ... @defer(if: $d) { sku total } } }",
        true,
        "a: 1, b: 1, c: 1",
        null)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id soft sku total } }",
        "query($d: Boolean!) { user(id: 1) { id soft ... @defer(if: $d) { sku total } } }",
        false,
        "a: 1, b: 1, c: 1",
        1)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { soft id sku total } }",
        "mutation { createUser(id: 1) { soft ... @defer { id sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { id soft __typename sku total } }",
        "mutation { createUser(id: 1) { id soft ... @defer { __typename sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUsers { soft id sku total } }",
        "mutation { createUsers { soft ... @defer { id sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { email id sku total } }",
        "mutation { createUser(id: 1) { email ... @defer { id sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id sku s: sku total } }",
        "{ user(id: 1) { id sku ... @defer { s: sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id k: sku sku total } }",
        "{ user(id: 1) { id k: sku ... @defer { sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id k: sku s: sku total } }",
        "{ user(id: 1) { id k: sku ... @defer { s: sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "mutation { createUser(id: 1) { soft t: __typename i: id s: sku total } }",
        "mutation { createUser(id: 1) { soft ... @defer { t: __typename i: id s: sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id label(prefix: \"a\") sku total } }",
        "{ user(id: 1) { id label(prefix: \"a\") ... @defer { label(prefix: \"a\") sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id x: label(prefix: \"a\") y: label(prefix: \"a\") sku total } }",
        "{ user(id: 1) { id x: label(prefix: \"a\") ... @defer { y: label(prefix: \"a\") sku total } } }",
        null,
        "a: 1, b: 1, c: 1",
        2)]
    [InlineData(
        "s2",
        "{ user(id: 1) { id x: label(prefix: \"a\") y: label(prefix: \"b\") sku total } }",
        "{ user(id: 1) { id x: label(prefix: \"a\") ... @defer { y: label(prefix: \"b\") sku total } } }",
        null,
        "a: 1, b: 2, c: 1",
        2)]
    public async Task Defer_Should_MatchPlainResult_When_DeferredGroupReadsRequirement(
        string schema,
        string plainQuery,
        string deferredQuery,
        bool? deferIf,
        string requestCounts,
        int? payloadCount)
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync(schema);
        var variables = deferIf is { } value ? new Dictionary<string, object?> { ["d"] = value } : null;

        // act
        var run = await RunRequireShapeAsync(gateway, plainQuery, deferredQuery, variables);

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
        Assert.Equal(requestCounts, run.RequestCounts);
        Assert.Equal(payloadCount ?? run.Payloads.Count, run.Payloads.Count);
    }

    [Fact]
    public async Task Defer_Should_DeliverHeldFieldValue_When_SiblingHeldFieldFails()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 5) { id soft sku total } }",
            "{ user(id: 5) { id ... @defer { soft sku total } } }");

        // assert
        run.DeferredData.ToJsonString().MatchInlineSnapshot(
            """
            {"user":{"id":5,"soft":null,"sku":"sku-5","total":"total-sku-5"}}
            """);
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 5
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "user",
                        "soft"
                      ]
                    }
                  ],
                  "data": {
                    "soft": null,
                    "sku": "sku-5",
                    "total": "total-sku-5"
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
    public async Task Defer_Should_DeliverHeldFieldValue_When_SiblingHeldFieldFailsOnListItem()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users5 { id soft sku total } }",
            "{ users5 { id ... @defer { soft sku total } } }");

        // assert
        run.DeferredData.ToJsonString().MatchInlineSnapshot(
            """
            {"users5":[{"id":1,"soft":"soft-1","sku":"sku-1","total":"total-sku-1"},{"id":5,"soft":null,"sku":"sku-5","total":"total-sku-5"},{"id":6,"soft":"soft-6","sku":"sku-6","total":"total-sku-6"}]}
            """);
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users5": [
                  {
                    "id": 1
                  },
                  {
                    "id": 5
                  },
                  {
                    "id": 6
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users5"
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
                    "sku": "sku-1",
                    "total": "total-sku-1"
                  }
                },
                {
                  "id": "0",
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "users5",
                        1,
                        "soft"
                      ]
                    }
                  ],
                  "subPath": [
                    1
                  ],
                  "data": {
                    "soft": null,
                    "sku": "sku-5",
                    "total": "total-sku-5"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    2
                  ],
                  "data": {
                    "soft": "soft-6",
                    "sku": "sku-6",
                    "total": "total-sku-6"
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
    public async Task Defer_Should_DeliverHeldFieldErrorInDeferredPayload_When_HeldFieldFails()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 5) { id soft total } }",
            "{ user(id: 5) { id ... @defer { soft total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 5
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "user",
                        "soft"
                      ]
                    }
                  ],
                  "data": {
                    "soft": null,
                    "total": "total-sku-5"
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
    public async Task Defer_Should_ReportHeldFieldErrorUnderDeferredName_When_HeldFieldIsAliased()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 5) { id sku t: soft s: soft total } }",
            "{ user(id: 5) { id sku t: soft ... @defer { s: soft total } } }");

        // assert
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "errors": [
                {
                  "message": "The soft failed.",
                  "path": [
                    "user",
                    "t"
                  ]
                }
              ],
              "data": {
                "user": {
                  "id": 5,
                  "sku": "sku-5",
                  "t": null
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "user",
                        "s"
                      ]
                    }
                  ],
                  "data": {
                    "s": null,
                    "total": "total-sku-5"
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
    public async Task Defer_Should_ReportHeldFieldErrorUnderDeferredName_When_PromotedHeldFieldIsAliased()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 5) { id s: soft sku total } }",
            "{ user(id: 5) { id ... @defer { s: soft sku total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 5
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "user",
                        "s"
                      ]
                    }
                  ],
                  "data": {
                    "s": null,
                    "sku": "sku-5",
                    "total": "total-sku-5"
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
    public async Task Defer_Should_DeliverHeldFieldErrorInDeferredPayload_When_HeldFieldFailsOnListItem()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users5 { id soft total } }",
            "{ users5 { id ... @defer { soft total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users5": [
                  {
                    "id": 1
                  },
                  {
                    "id": 5
                  },
                  {
                    "id": 6
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users5"
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "users5",
                        1,
                        "soft"
                      ]
                    }
                  ],
                  "subPath": [
                    1
                  ],
                  "data": {
                    "soft": null,
                    "total": "total-sku-5"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    2
                  ],
                  "data": {
                    "soft": "soft-6",
                    "total": "total-sku-6"
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
    public async Task Defer_Should_DeliverHeldFieldErrorInDeferredPayload_When_HeldFieldFailsInMutationResult()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "mutation { createUser(id: 5) { id soft total } }",
            "mutation { createUser(id: 5) { id ... @defer { soft total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "createUser": {
                  "id": 5
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "createUser",
                        "soft"
                      ]
                    }
                  ],
                  "data": {
                    "soft": null,
                    "total": "total-sku-5"
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
    public async Task Defer_Should_DeliverGroup_When_HeldFieldFailsOnLastListItem()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users15 { id soft total } }",
            "{ users15 { id ... @defer { soft total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users15": [
                  {
                    "id": 1
                  },
                  {
                    "id": 5
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users15"
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "users15",
                        1,
                        "soft"
                      ]
                    }
                  ],
                  "subPath": [
                    1
                  ],
                  "data": {
                    "soft": null,
                    "total": "total-sku-5"
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
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
    }

    [Fact]
    public async Task Defer_Should_DeliverEachHeldFieldError_When_FirstAndLastListItemFail()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users55 { id soft total } }",
            "{ users55 { id ... @defer { soft total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users55": [
                  {
                    "id": 5
                  },
                  {
                    "id": 1
                  },
                  {
                    "id": 5
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users55"
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
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "users55",
                        0,
                        "soft"
                      ]
                    }
                  ],
                  "subPath": [
                    0
                  ],
                  "data": {
                    "soft": null,
                    "total": "total-sku-5"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    1
                  ],
                  "data": {
                    "soft": "soft-1",
                    "total": "total-sku-1"
                  }
                },
                {
                  "id": "0",
                  "errors": [
                    {
                      "message": "The soft failed.",
                      "path": [
                        "users55",
                        2,
                        "soft"
                      ]
                    }
                  ],
                  "subPath": [
                    2
                  ],
                  "data": {
                    "soft": null,
                    "total": "total-sku-5"
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
    public async Task Defer_Should_DeliverErrorOnce_When_FailingListItemHasNoEntry()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users7 { id sku total } }",
            "{ users7 { id ... @defer { sku total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "users7": [
                  {
                    "id": 1
                  },
                  {
                    "id": 7
                  },
                  {
                    "id": 2
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users7"
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
                  "errors": [
                    {
                      "message": "The sku failed.",
                      "path": [
                        "users7",
                        1,
                        "sku"
                      ]
                    }
                  ],
                  "subPath": [
                    0
                  ],
                  "data": {
                    "sku": "sku-1",
                    "total": "total-sku-1"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    2
                  ],
                  "data": {
                    "sku": "sku-2",
                    "total": "total-sku-2"
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

    [Fact(Skip = "A deferred group below a list of lists is not delivered yet.")]
    public async Task Defer_Should_MatchPlainResult_When_DeferIsOnListOfLists()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ userGrid { id soft total } }",
            "{ userGrid { id ... @defer { soft total } } }");

        // assert
        Assert.True(JsonNode.DeepEquals(run.PlainData, run.DeferredData), run.DeferredData.ToJsonString());
    }

    [Fact]
    public async Task Defer_Should_FetchRequiredFieldOnce_When_ParentStepAlreadyFetchesItsSource()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ users5 { id soft sku total } }",
            "{ users5 { id soft ... @defer { soft sku total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "errors": [
                {
                  "message": "The soft failed.",
                  "path": [
                    "users5",
                    1,
                    "soft"
                  ]
                }
              ],
              "data": {
                "users5": [
                  {
                    "id": 1,
                    "soft": "soft-1"
                  },
                  {
                    "id": 5,
                    "soft": null
                  },
                  {
                    "id": 6,
                    "soft": "soft-6"
                  }
                ]
              },
              "pending": [
                {
                  "id": "0",
                  "path": [
                    "users5"
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
                    "sku": "sku-1",
                    "total": "total-sku-1"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    1
                  ],
                  "data": {
                    "sku": "sku-5",
                    "total": "total-sku-5"
                  }
                },
                {
                  "id": "0",
                  "subPath": [
                    2
                  ],
                  "data": {
                    "sku": "sku-6",
                    "total": "total-sku-6"
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
        run.RequestCounts.MatchInlineSnapshot("a: 1, b: 1, c: 1");
    }

    [Fact]
    public async Task Defer_Should_CompleteStream_When_SkuFailsUnderDeferredRequireField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var payloads = await ReadRequirePayloadsAsync(gateway, 7);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "errors": [
                {
                  "message": "The sku failed.",
                  "path": [
                    "user",
                    "sku"
                  ]
                }
              ],
              "data": {
                "user": {
                  "id": 7
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
    public async Task Defer_Should_KeepRequiredFieldErrorInInitialPayload_When_HeldFieldIsDeferredWithIt()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync("s2");

        // act
        var run = await RunRequireShapeAsync(
            gateway,
            "{ user(id: 7) { id soft total } }",
            "{ user(id: 7) { id ... @defer { soft total } } }");

        // assert
        run.Payloads.MatchInlineSnapshots(
        [
            """
            {
              "errors": [
                {
                  "message": "The sku failed.",
                  "path": [
                    "user",
                    "sku"
                  ]
                }
              ],
              "data": {
                "user": {
                  "id": 7
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
    public async Task Defer_Should_CompleteStream_When_LookupReturnsNullUnderDeferredRequireField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var payloads = await ReadRequirePayloadsAsync(gateway, 8);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 8
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
    public async Task Defer_Should_CompleteStream_When_LookupFailsUnderDeferredRequireField()
    {
        // arrange
        using var gateway = await CreateRequireGatewayAsync();

        // act
        var payloads = await ReadRequirePayloadsAsync(gateway, 9);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "user": {
                  "id": 9
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

    private static async Task<List<string>> ReadRequirePayloadsAsync(Gateway gateway, int userId)
    {
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());

        using var result = await client.PostAsync(
            new OperationRequest($"{{ user(id: {userId}) {{ id ... @defer {{ total }} }} }}"),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);

        return (await ReadPayloadsAsync(result))
            .ConvertAll(payload => JsonNode.Parse(payload)!.ToJsonString(s_indentedJson));
    }

    private Task<Gateway> CreateRequireGatewayAsync() => CreateRequireGatewayAsync("s1");

    private async Task<Gateway> CreateRequireGatewayAsync(string schema)
    {
        var serverA = CreateSourceSchema(
            "a",
            schema == "s2"
                ? b => b.AddQueryType<RequireSourceEmailA.Query>().AddMutationType<RequireSourceEmailA.Mutation>()
                : b => b.AddQueryType<RequireSourceA.Query>().AddMutationType<RequireSourceA.Mutation>());
        var serverB = CreateSourceSchema(
            "b",
            schema == "s4"
                ? b => b.AddQueryType<RequireSourceRootB.Query>()
                : b => b.AddQueryType<RequireSourceB.Query>());
        var serverC = CreateSourceSchema(
            "c",
            schema == "s2"
                ? b => b.AddQueryType<RequireSourceEmailC.Query>()
                : b => b.AddQueryType<RequireSourceC.Query>());

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
            [Shareable]
            public User? GetUser(int id) => new(id);

            public List<User?> GetUsers() => [new(1), new(2), new(3)];

            [Lookup]
            [Internal]
            public User? GetUserById(int id) => new(id);
        }

        public class Mutation
        {
            public User? CreateUser(int id) => new(id);

            public List<User?> CreateUsers() => [new(1), new(2), new(3)];
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
            public User? GetUserById(int id)
                => id switch
                {
                    8 => null,
                    9 => throw new GraphQLException("The b lookup failed."),
                    _ => new User(id)
                };
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? Sku => Id == 7 ? throw new GraphQLException("The sku failed.") : "sku-" + Id;

            public string? Soft => Id switch
            {
                5 => throw new GraphQLException("The soft failed."),
                _ => "soft-" + Id
            };

            public string? GetLabel(string? prefix) => prefix + "label-" + Id;
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

    private static class RequireSourceRootB
    {
        public class Query
        {
            [Shareable]
            public RequireSourceB.User? GetUser(int id) => new(id);

            [Lookup]
            [Internal]
            public RequireSourceB.User? GetUserById(int id) => new(id);
        }
    }

    private static class RequireSourceEmailA
    {
        public class Query
        {
            public User? GetUser(int id) => new(id);

            public List<User?> GetUsers() => [new(1), new(2), new(3)];

            public List<User?> GetUsers5() => [new(1), new(5), new(6)];

            public List<User?> GetUsers15() => [new(1), new(5)];

            public List<User?> GetUsers55() => [new(5), new(1), new(5)];

            public List<User?> GetUsers7() => [new(1), new(7), new(2)];

            public List<List<User?>?> GetUserGrid() => [[new(1), new(5)], [new(6)]];

            [Lookup]
            [Internal]
            public User? GetUserById(int id) => new(id);
        }

        public class Mutation
        {
            public User? CreateUser(int id) => new(id);

            public List<User?> CreateUsers() => [new(1), new(2), new(3)];
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            [Shareable]
            public string Email => "email-" + Id;
        }
    }

    private static class RequireSourceEmailC
    {
        public class Query
        {
            [Lookup]
            [Internal]
            public User? GetUserByEmail(string email) => new(email);
        }

        [EntityKey("email")]
        public record User(string Email)
        {
            public string? GetTotal([Require("sku")] string? sku) => "total-" + sku;
        }
    }
}
