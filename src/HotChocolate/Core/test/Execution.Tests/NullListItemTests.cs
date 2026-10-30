using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution;

public sealed class NullListItemTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ANonNullListOfNonNullObjectsHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ objects { id } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "objects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheList_When_ANullableListOfNonNullObjectsHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ nullableObjects { id } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableObjects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "nullableObjects": null
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ANonNullListOfNonNullScalarsHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ scalars }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "scalars",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheList_When_ANullableListOfNonNullScalarsHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ nullableScalars }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableScalars",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "nullableScalars": null
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_KeepTheNullItem_When_TheItemTypeIsNullable()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ lenientObjects { id } lenientScalars }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "lenientObjects": [
                  {
                    "id": 1
                  },
                  null,
                  {
                    "id": 3
                  }
                ],
                "lenientScalars": [
                  "a",
                  null,
                  "c"
                ]
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheNearestNullableParent_When_TheNullItemIsInANestedList()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ wrapper { name objects { id } } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "wrapper",
                    "objects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "wrapper": null
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_TheNullItemIsInAListOfNonNullLists()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ matrix }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "matrix",
                    1,
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ABatchResolverReturnsANullItemForANonNullList()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ batchObjects { id } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "batchObjects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheList_When_ABatchResolverReturnsANullItemForANullableList()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ nullableBatchObjects { id } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableBatchObjects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "nullableBatchObjects": null
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ABatchResolverReturnsANullScalarItemForANonNullList()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ batchScalars }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "batchScalars",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheParent_When_ABatchResolverOnAnObjectReturnsANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ wrapper { name batchObjects { id } } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "wrapper",
                    "batchObjects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "wrapper": null
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_AnItemIsNullBecauseANonNullChildFieldIsNull()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ brokenObjects { id broken } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "brokenObjects",
                    1,
                    "broken"
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_AnItemCannotBeResolvedToAnObjectType()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(executor, "{ things { ... on Item { id } } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Could not resolve the actual object type from `HotChocolate.Execution.NullListItemTests+Mystery` for the abstract type `things`.",
                  "path": [
                    "things",
                    1
                  ]
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheDeferredFragment_When_ThePlainListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ name ... @defer { objects { id } } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["objects",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheNullableList_When_TheDeferredListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ name ... @defer { nullableObjects { id } } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            incremental {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["nullableObjects",1],"extensions":{"code":"HC0018"}}],"data":{"nullableObjects":null}}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheDeferredFragment_When_TheDeferredBatchListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ name ... @defer { batchObjects { id } } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["batchObjects",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheDeferredFragment_When_TheDeferredScalarListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(executor, "{ name ... @defer { scalars } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query"}
            """,
            """
            pending {"id":"2","path":[]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["scalars",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheDeferredFragment_When_ADeferredNestedListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ name wrapper { name ... @defer { objects { id } } } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query","wrapper":{"name":"wrapper"}}
            """,
            """
            pending {"id":"2","path":["wrapper"]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["wrapper","objects",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ASyncStreamedNonNullListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(
            executor,
            "{ name objects @stream(initialCount: 0) { id } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "objects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheList_When_ASyncStreamedNullableListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(
            executor,
            "{ name nullableObjects @stream(initialCount: 0) { id } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "nullableObjects",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": {
                "name": "query",
                "nullableObjects": null
              }
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ASyncStreamedScalarListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var result = await ExecuteAsync(
            executor,
            "{ name scalars @stream(initialCount: 0) }");

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Cannot return null for non-nullable field.",
                  "path": [
                    "scalars",
                    1
                  ],
                  "extensions": {
                    "code": "HC0018"
                  }
                }
              ],
              "data": null
            }
            """);
    }

    [Fact]
    public async Task ExecuteAsync_Should_FailTheStream_When_AnAsyncStreamedNonNullListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ name asyncObjects @stream(initialCount: 0) { id } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query","asyncObjects":[]}
            """,
            """
            pending {"id":"2","path":["asyncObjects"]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["asyncObjects",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NullTheRoot_When_ANullItemIsInTheInitialItemsOfAnAsyncStream()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ name asyncObjects @stream(initialCount: 2) { id } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            errors {"message":"Cannot return null for non-nullable field.","path":["asyncObjects",1],"extensions":{"code":"HC0018"}}
            """,
            """
            data null
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_FailTheStream_When_AnAsyncStreamedNullableListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ name nullableAsyncObjects @stream(initialCount: 0) { id } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query","nullableAsyncObjects":[]}
            """,
            """
            pending {"id":"2","path":["nullableAsyncObjects"]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["nullableAsyncObjects",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_FailTheStream_When_AnAsyncStreamedScalarListHasANullItem()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ name asyncScalars @stream(initialCount: 0) }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query","asyncScalars":[]}
            """,
            """
            pending {"id":"2","path":["asyncScalars"]}
            """,
            """
            completed {"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["asyncScalars",1],"extensions":{"code":"HC0018"}}]}
            """,
            """
            hasNext false
            """
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_KeepTheNullItem_When_AnAsyncStreamedListHasNullableItems()
    {
        // arrange
        var executor = await CreateExecutorAsync();

        // act
        var events = await ExecuteIncrementalAsync(
            executor,
            "{ name lenientAsyncObjects @stream(initialCount: 0) { id } }");

        // assert
        events.MatchInlineSnapshots(
        [
            """
            data {"name":"query","lenientAsyncObjects":[]}
            """,
            """
            pending {"id":"2","path":["lenientAsyncObjects"]}
            """,
            """
            incremental {"id":"2","items":[{"id":1},null,{"id":3}]}
            """,
            """
            completed {"id":"2"}
            """,
            """
            hasNext false
            """
        ]);
    }

    private static async Task<string> ExecuteAsync(IRequestExecutor executor, string query)
    {
        await using var result = await executor
            .ExecuteAsync(query, TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        return result.ExpectOperationResult().ToJson();
    }

    private static Task<string[]> ExecuteIncrementalAsync(IRequestExecutor executor, string query)
    {
        return Task.Run(
                async () =>
                {
                    await using var result = await executor.ExecuteAsync(
                        query,
                        TestContext.Current.CancellationToken);
                    var events = new List<string>();
                    var hasNext = true;

                    await foreach (var payload in result
                        .ExpectResponseStream()
                        .ReadResultsAsync()
                        .WithCancellation(TestContext.Current.CancellationToken))
                    {
                        await using var payloadCleanup = payload;
                        using var document = JsonDocument.Parse(payload.ToJson(withIndentations: false));
                        var root = document.RootElement;

                        AddEvents(events, root, "errors", static e => $"errors {e.GetRawText()}");
                        AddEvents(events, root, "data", static d => $"data {d.GetRawText()}");
                        AddEvents(events, root, "pending", static p => $"pending {p.GetRawText()}");
                        AddEvents(events, root, "incremental", static i => $"incremental {i.GetRawText()}");
                        AddEvents(events, root, "completed", static c => $"completed {c.GetRawText()}");

                        hasNext = root.GetProperty("hasNext").GetBoolean();
                    }

                    events.Add($"hasNext {hasNext.ToString().ToLowerInvariant()}");
                    return events.ToArray();
                },
                TestContext.Current.CancellationToken)
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
    }

    private static void AddEvents(
        List<string> events,
        JsonElement root,
        string propertyName,
        Func<JsonElement, string> format)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return;
        }

        if (value.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                events.Add(format(item));
            }
        }
        else
        {
            events.Add(format(value));
        }
    }

    private static ValueTask<IRequestExecutor> CreateExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("name").Type<StringType>().Resolve("query");
                d.Field("objects").Type<NonNullType<ListType<NonNullType<ObjectType<Item>>>>>().Resolve(_ => Items());
                d.Field("nullableObjects").Type<ListType<NonNullType<ObjectType<Item>>>>().Resolve(_ => Items());
                d.Field("lenientObjects").Type<NonNullType<ListType<ObjectType<Item>>>>().Resolve(_ => Items());
                d.Field("scalars").Type<NonNullType<ListType<NonNullType<StringType>>>>().Resolve(_ => Scalars());
                d.Field("nullableScalars").Type<ListType<NonNullType<StringType>>>().Resolve(_ => Scalars());
                d.Field("lenientScalars").Type<NonNullType<ListType<StringType>>>().Resolve(_ => Scalars());
                d.Field("matrix")
                    .Type<NonNullType<ListType<NonNullType<ListType<NonNullType<StringType>>>>>>()
                    .Resolve(_ => new List<List<string?>> { new() { "a" }, new() { "b", null } });
                d.Field("brokenObjects")
                    .Type<NonNullType<ListType<NonNullType<ObjectType<Item>>>>>()
                    .Resolve(_ => new List<Item?> { new(1), new(2), new(3) });
                d.Field("things").Type<NonNullType<ListType<NonNullType<UnionType<Thing>>>>>()
                    .Resolve(_ => new List<object> { new Item(1), new Mystery(), new Item(3) });
                d.Field("asyncObjects")
                    .Type<NonNullType<ListType<NonNullType<ObjectType<Item>>>>>()
                    .Resolve(_ => AsyncItems());
                d.Field("nullableAsyncObjects")
                    .Type<ListType<NonNullType<ObjectType<Item>>>>()
                    .Resolve(_ => AsyncItems());
                d.Field("lenientAsyncObjects")
                    .Type<NonNullType<ListType<ObjectType<Item>>>>()
                    .Resolve(_ => AsyncItems());
                d.Field("asyncScalars")
                    .Type<NonNullType<ListType<NonNullType<StringType>>>>()
                    .Resolve(_ => AsyncScalars());
                d.Field("batchObjects")
                    .Type<NonNullType<ListType<NonNullType<ObjectType<Item>>>>>()
                    .ResolveBatch(ResolveBatchObjects);
                d.Field("nullableBatchObjects")
                    .Type<ListType<NonNullType<ObjectType<Item>>>>()
                    .ResolveBatch(ResolveBatchObjects);
                d.Field("batchScalars")
                    .Type<NonNullType<ListType<NonNullType<StringType>>>>()
                    .ResolveBatch(ResolveBatchScalars);
                d.Field("wrapper").Type<ObjectType<Wrapper>>().Resolve(_ => new Wrapper());
            })
            .AddObjectType<Wrapper>(d =>
            {
                d.Field("name").Type<NonNullType<StringType>>().Resolve("wrapper");
                d.Field("objects").Type<NonNullType<ListType<NonNullType<ObjectType<Item>>>>>().Resolve(_ => Items());
                d.Field("batchObjects")
                    .Type<NonNullType<ListType<NonNullType<ObjectType<Item>>>>>()
                    .ResolveBatch(ResolveBatchObjects);
            })
            .AddObjectType<Item>(d =>
            {
                d.Field(t => t.Id);
                d.Field("broken").Type<NonNullType<StringType>>().Resolve(ctx =>
                    ctx.Parent<Item>().Id == 2 ? null : "ok");
            })
            .AddUnionType<Thing>(d =>
            {
                d.Type<ObjectType<Item>>();
                d.ResolveAbstractType((ctx, obj) => obj is Item ? ctx.Schema.Types.GetType<ObjectType>("Item") : null);
            })
            .ModifyOptions(
                o =>
                {
                    o.EnableDefer = true;
                    o.EnableStream = true;
                })
            .BuildRequestExecutorAsync();

    private static List<Item?> Items() => [new(1), null, new(3)];

    private static List<string?> Scalars() => ["a", null, "c"];

    private static async IAsyncEnumerable<Item?> AsyncItems()
    {
        await Task.CompletedTask;
        yield return new Item(1);
        yield return null;
        yield return new Item(3);
    }

    private static async IAsyncEnumerable<string?> AsyncScalars()
    {
        await Task.CompletedTask;
        yield return "a";
        yield return null;
        yield return "c";
    }

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatchObjects(
        IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok(Items());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    private static ValueTask<IReadOnlyList<ResolverResult>> ResolveBatchScalars(
        IReadOnlyList<IResolverContext> contexts)
    {
        var results = new ResolverResult[contexts.Count];

        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = ResolverResult.Ok(Scalars());
        }

        return new ValueTask<IReadOnlyList<ResolverResult>>(results);
    }

    public sealed record Item(int Id);

    public sealed class Wrapper;

    public sealed class Mystery;

    public sealed class Thing;
}
