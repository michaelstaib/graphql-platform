using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using GreenDonut;
using GreenDonut.Data;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public class StreamTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// This test shows how IAsyncEnumerable is translated to SDL
    /// </summary>
    [Fact]
    public async Task Schema()
    {
        var executor = await DeferAndStreamTestSchema.CreateAsync();
        executor.Schema.MatchSnapshot();
    }

    [Fact]
    public async Task Stream()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            @"{
                ... @defer {
                    wait(m: 300)
                }
                persons @stream {
                    id
                }
            }",
            TestContext.Current.CancellationToken);

        Assert.IsType<ResponseStream>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_Nested_Defer()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            @"{
                ... @defer {
                    wait(m: 800)
                }
                personNodes(first: 1) {
                    nodes @stream {
                        ... @defer {
                            name
                        }
                    }
                }
            }",
            TestContext.Current.CancellationToken);

        Assert.IsType<ResponseStream>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_InitialCount_Set_To_1()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            @"{
                ... @defer {
                    wait(m: 300)
                }
                persons @stream(initialCount: 1) {
                    id
                }
            }",
            TestContext.Current.CancellationToken);

        Assert.IsType<ResponseStream>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_InitialCount_Exceeds_Total_Count()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            @"{
                ... @defer {
                    wait(m: 300)
                }
                persons @stream(initialCount: 7) {
                    id
                }
            }",
            TestContext.Current.CancellationToken);

        Assert.IsType<ResponseStream>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_Label_Set_To_abc()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            """
            {
                ... @defer {
                    wait(m: 300)
                }
                persons @stream(label: "abc") {
                    id
                }
            }
            """,
            TestContext.Current.CancellationToken);

        Assert.IsType<ResponseStream>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_If_Set_To_false()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            """
            {
                persons @stream(if: false) {
                    id
                }
            }
            """,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationResult>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_If_Variable_Set_To_false()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(
                    """
                    query ($stream: Boolean!) {
                        persons @stream(if: $stream) {
                            id
                        }
                    }
                    """)
                .SetVariableValues(new Dictionary<string, object?> { { "stream", false } })
                .Build(),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationResult>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_Should_BeRejected_When_IfIsNull()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            """
            {
                persons @stream(if: null) {
                    id
                }
            }
            """,
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The specified argument value does not match the argument type.",
                  "locations": [
                    {
                      "line": 2,
                      "column": 25
                    }
                  ],
                  "path": [
                    "persons"
                  ],
                  "extensions": {
                    "argument": "if",
                    "argumentValue": "null",
                    "locationType": "Boolean!",
                    "specifiedBy": "https://spec.graphql.org/September2025/#sec-Values-of-Correct-Type"
                  }
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Result()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(
                    """
                    {
                        persons {
                            id
                        }
                    }
                    """)
                .Build(),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationResult>(result).MatchSnapshot();
    }

    [Fact]
    public async Task Stream_Should_BeIgnored_When_FieldIsConnectionNodes()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ personNodes(first: 2) { nodes @stream(initialCount: 0) { id } } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "personNodes": {
                  "nodes": [
                    {
                      "id": "UGVyc29uOjE="
                    },
                    {
                      "id": "UGVyc29uOjI="
                    }
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_BeIgnored_When_FieldIsConnectionEdges()
    {
        // arrange
        var executor = await DeferAndStreamTestSchema.CreateAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ personNodes(first: 2) { edges @stream(initialCount: 0) { node { id } } } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "personNodes": {
                  "edges": [
                    {
                      "node": {
                        "id": "UGVyc29uOjE="
                      }
                    },
                    {
                      "node": {
                        "id": "UGVyc29uOjI="
                      }
                    }
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_BeIgnored_When_FieldIsPageConnectionNodesOrEdges()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            """
            {
                pagedItems {
                    nodes @stream(initialCount: 0) { id }
                    edges @stream(initialCount: 0) { node { id } }
                }
            }
            """,
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "pagedItems": {
                  "nodes": [
                    {
                      "id": 1
                    },
                    {
                      "id": 2
                    }
                  ],
                  "edges": [
                    {
                      "node": {
                        "id": 1
                      }
                    },
                    {
                      "node": {
                        "id": 2
                      }
                    }
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_BeIgnored_When_AsyncFieldIsFlaggedAsConnectionNodesOrEdges()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            """
            {
                connectionLike {
                    nodes @stream(initialCount: 0) { id }
                    edges @stream(initialCount: 0) { id }
                }
            }
            """,
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "connectionLike": {
                  "nodes": [
                    {
                      "id": 1
                    },
                    {
                      "id": 2
                    }
                  ],
                  "edges": [
                    {
                      "id": 1
                    },
                    {
                      "id": 2
                    }
                  ]
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_DeliverItemsReadyInOnePassInOnePayload_When_EnumeratorYieldsSynchronously()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ burstItems @stream(initialCount: 1) { id } }",
            state.ReleaseNextItem);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "burstItems": [
                  {
                    "id": 1
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "burstItems"
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
                  "id": "2",
                  "items": [
                    {
                      "id": 2
                    },
                    {
                      "id": 3
                    },
                    {
                      "id": 4
                    }
                  ]
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
    public async Task Stream_Should_DeliverOnePayloadPerItem_When_ItemsAreProducedAsynchronously()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ gatedItems @stream(initialCount: 1) { id } }",
            state.ReleaseNextItem,
            state.ReleaseNextItem);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "gatedItems": [
                  {
                    "id": 1
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "gatedItems"
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
                  "id": "2",
                  "items": [
                    {
                      "id": 2
                    }
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
                  "id": "2",
                  "items": [
                    {
                      "id": 3
                    }
                  ]
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
    public async Task Stream_Should_BatchChildResolvers_When_ItemsAreReadyInOnePass()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ lateBurstItems @stream { id loaded } }",
            state.ReleaseNextItem);

        // assert
        Assert.Equal(["1,2,3,4"], state.Batches);
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "lateBurstItems": []
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "lateBurstItems"
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
                  "id": "2",
                  "items": [
                    {
                      "id": 1,
                      "loaded": "loaded-1"
                    },
                    {
                      "id": 2,
                      "loaded": "loaded-2"
                    },
                    {
                      "id": 3,
                      "loaded": "loaded-3"
                    },
                    {
                      "id": 4,
                      "loaded": "loaded-4"
                    }
                  ]
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
    public async Task Stream_Should_ExecuteNestedDefer_When_ItemsDeferFields()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            """
            {
                burstItems(count: 2) @stream(initialCount: 1) {
                    id
                    ... @defer {
                        gatedName
                    }
                }
            }
            """,
            state.ReleaseNextItem,
            () => state.ReleaseName(1),
            () => state.ReleaseName(2));

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "burstItems": [
                  {
                    "id": 1
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "burstItems",
                    0
                  ]
                },
                {
                  "id": "3",
                  "path": [
                    "burstItems"
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
                  "id": "5",
                  "path": [
                    "burstItems",
                    1
                  ]
                }
              ],
              "incremental": [
                {
                  "id": "3",
                  "items": [
                    {
                      "id": 2
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "3"
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
                    "gatedName": "item-1"
                  }
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "5",
                  "data": {
                    "gatedName": "item-2"
                  }
                }
              ],
              "completed": [
                {
                  "id": "5"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Stream_Should_Stream_When_ListIsInsideDeferredFragment()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ ... @defer { items(count: 3) @stream(initialCount: 1) { id } } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ResponseStream>(result).MatchInlineSnapshot(
            """
            {
              "data": {},
              "pending": [
                {
                  "id": "2",
                  "path": []
                },
                {
                  "id": "3",
                  "path": [
                    "items"
                  ]
                }
              ],
              "incremental": [
                {
                  "id": "2",
                  "data": {
                    "items": [
                      {
                        "id": 1
                      }
                    ]
                  }
                },
                {
                  "id": "3",
                  "items": [
                    {
                      "id": 2
                    },
                    {
                      "id": 3
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2"
                },
                {
                  "id": "3"
                }
              ],
              "hasNext": false
            }

            """);
    }

    [Fact]
    public async Task Stream_Should_StreamEachList_When_ListsAreInsideListItems()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ items(count: 2) { id gatedChildren @stream(initialCount: 1) { id } } }",
            () => state.ReleaseChildren(1),
            () => state.ReleaseChildren(2));

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "items": [
                  {
                    "id": 1,
                    "gatedChildren": [
                      {
                        "id": 11
                      }
                    ]
                  },
                  {
                    "id": 2,
                    "gatedChildren": [
                      {
                        "id": 21
                      }
                    ]
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "items",
                    0,
                    "gatedChildren"
                  ]
                },
                {
                  "id": "3",
                  "path": [
                    "items",
                    1,
                    "gatedChildren"
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
                  "id": "2",
                  "items": [
                    {
                      "id": 12
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "3",
                  "items": [
                    {
                      "id": 22
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "3"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Stream_Should_StreamNestedLists_When_StreamedItemsStreamTheirChildren()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ burstItems(count: 2) @stream(initialCount: 1) { id gatedChildren @stream(initialCount: 1) { id } } }",
            state.ReleaseNextItem,
            () => state.ReleaseChildren(1),
            () => state.ReleaseChildren(2));

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "burstItems": [
                  {
                    "id": 1,
                    "gatedChildren": [
                      {
                        "id": 11
                      }
                    ]
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "burstItems"
                  ]
                },
                {
                  "id": "3",
                  "path": [
                    "burstItems",
                    0,
                    "gatedChildren"
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
                  "id": "5",
                  "path": [
                    "burstItems",
                    1,
                    "gatedChildren"
                  ]
                }
              ],
              "incremental": [
                {
                  "id": "2",
                  "items": [
                    {
                      "id": 2,
                      "gatedChildren": [
                        {
                          "id": 21
                        }
                      ]
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "3",
                  "items": [
                    {
                      "id": 12
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "3"
                }
              ],
              "hasNext": true
            }
            """,
            """
            {
              "incremental": [
                {
                  "id": "5",
                  "items": [
                    {
                      "id": 22
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "5"
                }
              ],
              "hasNext": false
            }
            """
        ]);
    }

    [Fact]
    public async Task Stream_Should_DeliverScalarItems_When_ListHasLeafElements()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);

        // act
        var payloads = await ReadPayloadsAsync(
            executor,
            "{ burstNames @stream(initialCount: 1) }",
            state.ReleaseNextItem);

        // assert
        payloads.MatchInlineSnapshots(
        [
            """
            {
              "data": {
                "burstNames": [
                  "a"
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "burstNames"
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
                  "id": "2",
                  "items": [
                    "b",
                    "c"
                  ]
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
    public async Task Stream_Should_ReportItemErrorWithItemPath_When_ItemFieldIsNullable()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ items(count: 4) @stream(initialCount: 1) { id flaky } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ResponseStream>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "items": [
                  {
                    "id": 1,
                    "flaky": "ok-1"
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "items"
                  ]
                }
              ],
              "incremental": [
                {
                  "id": "2",
                  "items": [
                    {
                      "id": 2,
                      "flaky": "ok-2"
                    },
                    {
                      "id": 3,
                      "flaky": null
                    },
                    {
                      "id": 4,
                      "flaky": "ok-4"
                    }
                  ],
                  "errors": [
                    {
                      "message": "flaky item",
                      "path": [
                        "items",
                        2,
                        "flaky"
                      ]
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": false
            }

            """);
    }

    [Fact]
    public async Task Stream_Should_CompleteWithErrors_When_ItemErrorBubblesAboveTheList()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ strictItems(count: 4) @stream(initialCount: 1) { id strict } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ResponseStream>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "strictItems": [
                  {
                    "id": 1,
                    "strict": "ok-1"
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "strictItems"
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2",
                  "errors": [
                    {
                      "message": "strict item",
                      "path": [
                        "strictItems",
                        2,
                        "strict"
                      ]
                    }
                  ]
                }
              ],
              "hasNext": false
            }

            """);
    }

    [Fact]
    public async Task Stream_Should_CompleteWithErrors_When_EnumeratorThrows()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ failingItems @stream(initialCount: 1) { id } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ResponseStream>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "failingItems": [
                  {
                    "id": 1
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "failingItems"
                  ]
                }
              ],
              "incremental": [
                {
                  "id": "2",
                  "items": [
                    {
                      "id": 2
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2",
                  "errors": [
                    {
                      "message": "enumerator failed",
                      "path": [
                        "failingItems"
                      ]
                    }
                  ]
                }
              ],
              "hasNext": false
            }

            """);
    }

    [Fact]
    public async Task Stream_Should_CompleteWithoutItems_When_InitialCountEqualsListSize()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ items(count: 2) @stream(initialCount: 2) { id } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ResponseStream>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "items": [
                  {
                    "id": 1
                  },
                  {
                    "id": 2
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "items"
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": false
            }

            """);
    }

    [Fact]
    public async Task Stream_Should_NotStream_When_ListEndsWithinInitialCount()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ items(count: 2) @stream(initialCount: 3) { id } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "items": [
                  {
                    "id": 1
                  },
                  {
                    "id": 2
                  }
                ]
              }
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_StreamWithLabel_When_InitialCountAndIfAreVariables()
    {
        // arrange
        var executor = await CreateExecutorAsync();
        var request = OperationRequestBuilder
            .New()
            .SetDocument(
                """
                query ($initialCount: Int!, $stream: Boolean!) {
                    items(count: 3) @stream(label: "fromQuery", initialCount: $initialCount, if: $stream) {
                        id
                    }
                }
                """)
            .SetVariableValues(
                new Dictionary<string, object?>
                {
                    ["initialCount"] = 1,
                    ["stream"] = true
                })
            .Build();

        // act
        var result = await executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<ResponseStream>(result).MatchInlineSnapshot(
            """
            {
              "data": {
                "items": [
                  {
                    "id": 1
                  }
                ]
              },
              "pending": [
                {
                  "id": "2",
                  "path": [
                    "items"
                  ],
                  "label": "fromQuery"
                }
              ],
              "incremental": [
                {
                  "id": "2",
                  "items": [
                    {
                      "id": 2
                    },
                    {
                      "id": 3
                    }
                  ]
                }
              ],
              "completed": [
                {
                  "id": "2"
                }
              ],
              "hasNext": false
            }

            """);
    }

    [Fact]
    public async Task Stream_Should_RaiseFieldError_When_InitialCountIsNegative()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ items(count: 2) @stream(initialCount: -1) { id } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "The `initialCount` argument of the `@stream` directive must not be negative.",
                  "path": [
                    "items"
                  ],
                  "extensions": {
                    "code": "EXEC_INVALID_STREAM_INITIAL_COUNT"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_RaiseFieldError_When_EnumeratorThrowsWhileReadingInitialItems()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await executor.ExecuteAsync(
            "{ failingItems @stream(initialCount: 3) { id } }",
            TestContext.Current.CancellationToken);

        // assert
        Assert.IsType<OperationResult>(result).MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "enumerator failed",
                  "path": [
                    "failingItems"
                  ]
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task Stream_Should_DisposeEnumeratorOnce_When_ResponseStreamIsDisposedMidStream()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);
        var result = await executor.ExecuteAsync(
            "{ endlessItems @stream(initialCount: 1) { id } }",
            TestContext.Current.CancellationToken);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await using (var enumerator = stream
            .ReadResultsAsync()
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            Assert.True(await enumerator.MoveNextAsync());
            await enumerator.Current.DisposeAsync();
        }

        await stream.DisposeAsync();
        await state.EnumeratorDisposed.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(1, state.EnumeratorDisposeCalls);
    }

    [Fact]
    public async Task Stream_Should_DisposeEnumeratorOnce_When_RequestIsAborted()
    {
        // arrange
        var state = new StreamState();
        var executor = await CreateExecutorAsync(state);
        using var requestAborted = new CancellationTokenSource();
        var result = await executor.ExecuteAsync(
            "{ endlessItems @stream(initialCount: 1) { id } }",
            requestAborted.Token);
        var stream = Assert.IsType<ResponseStream>(result);

        // act
        await requestAborted.CancelAsync();
        await state.EnumeratorDisposed.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        await stream.DisposeAsync();

        // assert
        Assert.Equal(1, state.EnumeratorDisposeCalls);
    }

    private static async Task<IRequestExecutor> CreateExecutorAsync(StreamState? state = null)
        => await new ServiceCollection()
            .AddSingleton(state ?? new StreamState())
            .AddGraphQL()
            .AddQueryType<StreamQuery>()
            .AddType<ConnectionLikeType>()
            .AddDataLoader<StreamLoadedDataLoader>()
            .ModifyOptions(
                o =>
                {
                    o.EnableDefer = true;
                    o.EnableStream = true;
                })
            .BuildRequestExecutorAsync();

    // Runs one step after each payload that was read, which lets a test hold back the producers of
    // later payloads until the earlier payloads were observed.
    private static async Task<List<string>> ReadPayloadsAsync(
        IRequestExecutor executor,
        string query,
        params Action[] steps)
    {
        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
        var payloads = new List<string>();
        var nextStep = 0;

        await foreach (var payload in Assert.IsType<ResponseStream>(result)
            .ReadResultsAsync()
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            await using var cleanup = payload;
            payloads.Add(payload.ToJson());

            if (nextStep < steps.Length)
            {
                steps[nextStep++]();
            }
        }

        return payloads;
    }

    public sealed class StreamState
    {
        private readonly SemaphoreSlim _items = new(0);
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _names = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _children = new();
        private readonly List<string> _batches = [];
        private int _enumeratorDisposeCalls;

        public IReadOnlyList<string> Batches
        {
            get
            {
                lock (_batches)
                {
                    return [.. _batches];
                }
            }
        }

        public int EnumeratorDisposeCalls => Volatile.Read(ref _enumeratorDisposeCalls);

        public TaskCompletionSource EnumeratorDisposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseNextItem() => _items.Release();

        public Task WaitForNextItemAsync(CancellationToken cancellationToken) => _items.WaitAsync(cancellationToken);

        public void ReleaseName(int id) => GetGate(_names, id).TrySetResult();

        public Task WaitForNameAsync(int id) => GetGate(_names, id).Task;

        public void ReleaseChildren(int id) => GetGate(_children, id).TrySetResult();

        public Task WaitForChildrenAsync(int id) => GetGate(_children, id).Task;

        public void AddBatch(IReadOnlyList<int> keys)
        {
            lock (_batches)
            {
                _batches.Add(string.Join(",", keys.Order()));
            }
        }

        public void EnumeratorDisposedCalled()
        {
            Interlocked.Increment(ref _enumeratorDisposeCalls);
            EnumeratorDisposed.TrySetResult();
        }

        private static TaskCompletionSource GetGate(ConcurrentDictionary<int, TaskCompletionSource> gates, int id)
            => gates.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public sealed class StreamLoadedDataLoader(
        StreamState state,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : BatchDataLoader<int, string>(batchScheduler, options)
    {
        protected override Task<IReadOnlyDictionary<int, string>> LoadBatchAsync(
            IReadOnlyList<int> keys,
            CancellationToken cancellationToken)
        {
            state.AddBatch(keys);
            return Task.FromResult<IReadOnlyDictionary<int, string>>(keys.ToDictionary(k => k, k => $"loaded-{k}"));
        }
    }

    public sealed class Item(int id)
    {
        public int Id => id;

        public async Task<string> GetGatedNameAsync([Service] StreamState state)
        {
            await state.WaitForNameAsync(id);
            return $"item-{id}";
        }

        public async IAsyncEnumerable<Item> GetGatedChildren(
            [Service] StreamState state,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new Item((id * 10) + 1);

            await state.WaitForChildrenAsync(id).WaitAsync(cancellationToken);

            yield return new Item((id * 10) + 2);
        }

        public async Task<string> GetLoadedAsync(StreamLoadedDataLoader loader, CancellationToken cancellationToken)
            => await loader.LoadRequiredAsync(id, cancellationToken);

        public async Task<string?> GetFlakyAsync()
        {
            await Task.Yield();

            return id == 3
                ? throw new GraphQLException("flaky item")
                : $"ok-{id}";
        }

        public async Task<string> GetStrictAsync()
        {
            await Task.Yield();

            return id == 3
                ? throw new GraphQLException("strict item")
                : $"ok-{id}";
        }
    }

    public sealed class StreamQuery
    {
        public async Task<List<Item>> GetItemsAsync(int count)
        {
            await Task.Yield();
            return [.. Enumerable.Range(1, count).Select(i => new Item(i))];
        }

        [GraphQLType<ListType<NonNullType<ObjectType<Item>>>>]
        public async Task<List<Item>> GetStrictItemsAsync(int count)
        {
            await Task.Yield();
            return [.. Enumerable.Range(1, count).Select(i => new Item(i))];
        }

        public async IAsyncEnumerable<Item> GetBurstItems(
            [Service] StreamState state,
            [EnumeratorCancellation] CancellationToken cancellationToken,
            int count = 4)
        {
            yield return new Item(1);

            await state.WaitForNextItemAsync(cancellationToken);

            for (var i = 2; i <= count; i++)
            {
                yield return new Item(i);
            }
        }

        [GraphQLType<NonNullType<ObjectType<PageConnection<Item>>>>]
        public PageConnection<Item> GetPagedItems()
            => new(Page<Item>.Create(
                [new Item(1), new Item(2)],
                hasNextPage: false,
                hasPreviousPage: false,
                item => item.Id.ToString()));

        public ConnectionLike GetConnectionLike()
            => new();

        public async IAsyncEnumerable<string> GetBurstNames(
            [Service] StreamState state,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return "a";

            await state.WaitForNextItemAsync(cancellationToken);

            yield return "b";
            yield return "c";
        }

        public async IAsyncEnumerable<Item> GetLateBurstItems(
            [Service] StreamState state,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await state.WaitForNextItemAsync(cancellationToken);

            for (var i = 1; i <= 4; i++)
            {
                yield return new Item(i);
            }
        }

        public async IAsyncEnumerable<Item> GetGatedItems(
            [Service] StreamState state,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new Item(1);

            for (var i = 2; i <= 3; i++)
            {
                await state.WaitForNextItemAsync(cancellationToken);
                yield return new Item(i);
            }
        }

        public async IAsyncEnumerable<Item> GetFailingItems(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new Item(1);
            yield return new Item(2);
            await Task.Delay(10, cancellationToken);
            throw new GraphQLException("enumerator failed");
        }

        public IAsyncEnumerable<Item> GetEndlessItems([Service] StreamState state)
            => new EndlessItems(state);
    }

    public sealed class ConnectionLike
    {
        public async IAsyncEnumerable<Item> GetNodes()
        {
            yield return new Item(1);
            await Task.Yield();
            yield return new Item(2);
        }

        public async IAsyncEnumerable<Item> GetEdges()
        {
            yield return new Item(1);
            await Task.Yield();
            yield return new Item(2);
        }
    }

    public sealed class ConnectionLikeType : ObjectType<ConnectionLike>
    {
        protected override void Configure(IObjectTypeDescriptor<ConnectionLike> descriptor)
        {
            descriptor
                .Field(t => t.GetNodes())
                .Extend()
                .OnBeforeCreate((_, field) => field.SetConnectionNodesFieldFlags());

            descriptor
                .Field(t => t.GetEdges())
                .Extend()
                .OnBeforeCreate((_, field) => field.SetConnectionEdgesFieldFlags());
        }
    }

    private sealed class EndlessItems(StreamState state) : IAsyncEnumerable<Item>
    {
        public IAsyncEnumerator<Item> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Enumerator(state, cancellationToken);

        private sealed class Enumerator(StreamState state, CancellationToken cancellationToken)
            : IAsyncEnumerator<Item>
        {
            private int _index;

            public Item Current { get; private set; } = null!;

            public async ValueTask<bool> MoveNextAsync()
            {
                if (_index > 0)
                {
                    await Task.Delay(10, cancellationToken);
                }

                Current = new Item(++_index);
                return true;
            }

            public ValueTask DisposeAsync()
            {
                state.EnumeratorDisposedCalled();
                return ValueTask.CompletedTask;
            }
        }
    }
}
