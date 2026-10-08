using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization;
using HotChocolate.Fusion.Authorization.InMemory;

namespace HotChocolate.Fusion.Execution;

public class NestedDeferDeliveryTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          product: Product
        }

        type Product {
          id: ID!
          name: String
          price: Int
          cost: Int
        }
        """;

    private const string Data =
        """
        {
          "product": { "id": "1", "name": "Shoe", "price": 10, "cost": 5 }
        }
        """;

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheInnerDeferAsOwnPayload_When_TheOuterDeferIsDisabled()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean!) { product { id ... @defer(if: $d) { price ... @defer { cost } } } }")
            .SetVariableValues("""{"d":false}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1",
                      "price": 10
                    }
                  },
                  "pending": [
                    {
                      "id": "1",
                      "path": [
                        "product"
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
                      "id": "1",
                      "data": {
                        "cost": 5
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "1"
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheInnerDeferAsOwnPayload_When_TheEnabledInnerDeferIsConditional()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean! $e: Boolean!) "
                + "{ product { id ... @defer(if: $d) { price ... @defer(if: $e) { cost } } } }")
            .SetVariableValues("""{"d":false,"e":true}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1",
                      "price": 10
                    }
                  },
                  "pending": [
                    {
                      "id": "1",
                      "path": [
                        "product"
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
                      "id": "1",
                      "data": {
                        "cost": 5
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "1"
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheInnerDeferAfterTheActiveAncestor_When_ADeferInBetweenIsDisabled()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean!) "
                + "{ product { id ... @defer { name ... @defer(if: $d) { ... @defer { cost } } } } }")
            .SetVariableValues("""{"d":false}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1"
                    }
                  },
                  "pending": [
                    {
                      "id": "0",
                      "path": [
                        "product"
                      ]
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "pending": [
                    {
                      "id": "2",
                      "path": [
                        "product"
                      ]
                    }
                  ],
                  "incremental": [
                    {
                      "id": "0",
                      "data": {
                        "name": "Shoe"
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "0"
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
                      "data": {
                        "cost": 5
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

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheConditionalInnerDeferWithItsParent_When_TheInnerConditionIsFalse()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean!) "
                + "{ product { id ... @defer { name ... @defer(if: $d) { price } } } }")
            .SetVariableValues("""{"d":false}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1"
                    }
                  },
                  "pending": [
                    {
                      "id": "0",
                      "path": [
                        "product"
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
                        "name": "Shoe",
                        "price": 10
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
    public async Task ExecuteAsync_Should_DeliverTheConditionalInnerDeferAfterItsParent_When_TheInnerConditionIsTrue()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean!) "
                + "{ product { id ... @defer { name ... @defer(if: $d) { price } } } }")
            .SetVariableValues("""{"d":true}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1"
                    }
                  },
                  "pending": [
                    {
                      "id": "0",
                      "path": [
                        "product"
                      ]
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "pending": [
                    {
                      "id": "1",
                      "path": [
                        "product"
                      ]
                    }
                  ],
                  "incremental": [
                    {
                      "id": "0",
                      "data": {
                        "name": "Shoe"
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "0"
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "incremental": [
                    {
                      "id": "1",
                      "data": {
                        "price": 10
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "1"
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheConditionalInnerDeferWithItsParent_When_BothConditionsAreMixed()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean! $e: Boolean!) "
                + "{ product { id ... @defer(if: $d) { name ... @defer(if: $e) { price } } } }")
            .SetVariableValues("""{"d":true,"e":false}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1"
                    }
                  },
                  "pending": [
                    {
                      "id": "0",
                      "path": [
                        "product"
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
                        "name": "Shoe",
                        "price": 10
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
    public async Task ExecuteAsync_Should_DeliverEveryFieldInTheInitialResult_When_BothConditionsAreFalse()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean! $e: Boolean!) "
                + "{ product { id ... @defer(if: $d) { name ... @defer(if: $e) { price } } } }")
            .SetVariableValues("""{"d":false,"e":false}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1",
                      "name": "Shoe",
                      "price": 10
                    }
                  },
                  "hasNext": false
                }
                """
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DeliverTheConditionalInnerDeferWithItsParent_When_TheParentHasNoOwnFields()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean!) { product { id ... @defer { ... @defer(if: $d) { price } } } }")
            .SetVariableValues("""{"d":false}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1"
                    }
                  },
                  "pending": [
                    {
                      "id": "0",
                      "path": [
                        "product"
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
                        "price": 10
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
    public async Task ExecuteAsync_Should_DeliverTheConditionalInnerDeferAfterItsParent_When_TheParentHasNoOwnFields()
    {
        // arrange
        var executor = await CreateNestedDeferExecutorAsync();
        var request = CreateRequest(
                "query($d: Boolean!) { product { id ... @defer { ... @defer(if: $d) { price } } } }")
            .SetVariableValues("""{"d":true}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);
        var payloads = await ReadPayloadsAsync(result, TestContext.Current.CancellationToken);

        // assert
        payloads.MatchInlineSnapshots(
            [
                """
                {
                  "data": {
                    "product": {
                      "id": "1"
                    }
                  },
                  "pending": [
                    {
                      "id": "0",
                      "path": [
                        "product"
                      ]
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "pending": [
                    {
                      "id": "1",
                      "path": [
                        "product"
                      ]
                    }
                  ],
                  "incremental": [
                    {
                      "id": "0",
                      "data": {}
                    }
                  ],
                  "completed": [
                    {
                      "id": "0"
                    }
                  ],
                  "hasNext": true
                }
                """,
                """
                {
                  "incremental": [
                    {
                      "id": "1",
                      "data": {
                        "price": 10
                      }
                    }
                  ],
                  "completed": [
                    {
                      "id": "1"
                    }
                  ],
                  "hasNext": false
                }
                """
            ]);
    }

    private static Task<IRequestExecutor> CreateNestedDeferExecutorAsync()
        => CreateExecutorAsync(Schema, new AuthorizationTestClient(Data), new InMemoryPolicyRecorder());
}
