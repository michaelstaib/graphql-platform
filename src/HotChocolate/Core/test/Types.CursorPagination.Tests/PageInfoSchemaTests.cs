using GreenDonut.Data;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Types.Pagination;

public class PageInfoSchemaTests
{
    [Fact]
    public async Task Schema_Should_KeepPageInfoFieldNames_When_StreamPageConnectionIsExposed()
    {
        // act
        var schema = await new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .BuildSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        schema.Types.GetType<ObjectType>("PageInfo").ToString().MatchInlineSnapshot(
            """
            "Information about pagination in a connection."
            type PageInfo {
              "Indicates whether more edges exist following the set defined by the clients arguments."
              hasNextPage: Boolean! @cost(weight: "10")
              "Indicates whether more edges exist prior the set defined by the clients arguments."
              hasPreviousPage: Boolean! @cost(weight: "10")
              "When paginating backwards, the cursor to continue."
              startCursor: String @cost(weight: "10")
              "When paginating forwards, the cursor to continue."
              endCursor: String @cost(weight: "10")
              "A list of cursors to continue paginating forwards."
              forwardCursors: [PageCursor!]! @cost(weight: "10")
              "A list of cursors to continue paginating backwards."
              backwardCursors: [PageCursor!]! @cost(weight: "10")
            }
            """);
    }

    [Fact]
    public async Task Schema_Should_ApplyShareableOnce_When_ClassicPageInfoIsComposite()
    {
        // arrange
        var builder = new ServiceCollection()
            .AddGraphQLServer()
            .ModifyOptions(o => o.ApplyShareableToPageInfo = true)
            .AddQueryType<ClassicQuery>();

        // act
        var schema = await builder.BuildSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        schema.Types.GetType<ObjectType>("PageInfo").ToString().MatchInlineSnapshot(
            """
            "Information about pagination in a connection."
            type PageInfo @shareable {
              "Indicates whether more edges exist following the set defined by the clients arguments."
              hasNextPage: Boolean! @cost(weight: "10")
              "Indicates whether more edges exist prior the set defined by the clients arguments."
              hasPreviousPage: Boolean! @cost(weight: "10")
              "When paginating backwards, the cursor to continue."
              startCursor: String @cost(weight: "10")
              "When paginating forwards, the cursor to continue."
              endCursor: String @cost(weight: "10")
              "A list of cursors to continue paginating forwards."
              forwardCursors: [PageCursor!]! @cost(weight: "10")
              "A list of cursors to continue paginating backwards."
              backwardCursors: [PageCursor!]! @cost(weight: "10")
            }
            """);
    }

    [Fact]
    public async Task Schema_Should_ApplyShareableOnce_When_StreamPageInfoIsComposite()
    {
        // act
        var schema = await new ServiceCollection()
            .AddGraphQLServer()
            .ModifyOptions(o => o.ApplyShareableToPageInfo = true)
            .AddQueryType<Query>()
            .BuildSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        schema.Types.GetType<ObjectType>("PageInfo").ToString().MatchInlineSnapshot(
            """
            "Information about pagination in a connection."
            type PageInfo @shareable {
              "Indicates whether more edges exist following the set defined by the clients arguments."
              hasNextPage: Boolean! @cost(weight: "10")
              "Indicates whether more edges exist prior the set defined by the clients arguments."
              hasPreviousPage: Boolean! @cost(weight: "10")
              "When paginating backwards, the cursor to continue."
              startCursor: String @cost(weight: "10")
              "When paginating forwards, the cursor to continue."
              endCursor: String @cost(weight: "10")
              "A list of cursors to continue paginating forwards."
              forwardCursors: [PageCursor!]! @cost(weight: "10")
              "A list of cursors to continue paginating backwards."
              backwardCursors: [PageCursor!]! @cost(weight: "10")
            }
            """);
    }

    [Fact]
    public async Task PageInfo_Should_ResolveAllFields_When_QueriedOverStreamPageConnection()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync(
            """
            {
              streamed {
                pageInfo {
                  hasNextPage
                  hasPreviousPage
                  startCursor
                  endCursor
                  forwardCursors { page }
                  backwardCursors { page }
                }
              }
            }
            """,
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "streamed": {
                  "pageInfo": {
                    "hasNextPage": false,
                    "hasPreviousPage": false,
                    "startCursor": null,
                    "endCursor": null,
                    "forwardCursors": [],
                    "backwardCursors": []
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Schema_Should_ExposeOnePageInfoType_When_ClassicPageConnectionAndStreamPageConnectionAreMixed()
    {
        // arrange
        var builder = new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<MixedQuery>();

        // act
        var schema = await builder.BuildSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        schema.Types.GetType<ObjectType>("PageInfo").ToString().MatchInlineSnapshot(
            """
            "Information about pagination in a connection."
            type PageInfo {
              "Indicates whether more edges exist following the set defined by the clients arguments."
              hasNextPage: Boolean! @cost(weight: "10")
              "Indicates whether more edges exist prior the set defined by the clients arguments."
              hasPreviousPage: Boolean! @cost(weight: "10")
              "When paginating backwards, the cursor to continue."
              startCursor: String @cost(weight: "10")
              "When paginating forwards, the cursor to continue."
              endCursor: String @cost(weight: "10")
              "A list of cursors to continue paginating forwards."
              forwardCursors: [PageCursor!]! @cost(weight: "10")
              "A list of cursors to continue paginating backwards."
              backwardCursors: [PageCursor!]! @cost(weight: "10")
            }
            """);
    }

    [Fact]
    public async Task PageInfo_Should_ResolveEmptyCursorLists_When_QueriedOverClassicConnection()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<ClassicQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync(
            """
            {
              products {
                pageInfo {
                  hasNextPage
                  hasPreviousPage
                  startCursor
                  endCursor
                  forwardCursors { page }
                  backwardCursors { page }
                }
              }
            }
            """,
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "products": {
                  "pageInfo": {
                    "hasNextPage": false,
                    "hasPreviousPage": false,
                    "startCursor": null,
                    "endCursor": null,
                    "forwardCursors": [],
                    "backwardCursors": []
                  }
                }
              }
            }
            """);
    }

    public sealed class Product
    {
        public int Id { get; set; }
    }

    public sealed class ClassicQuery
    {
        [UsePaging]
        public IEnumerable<Product> GetProducts() => [];
    }

    public sealed class MixedQuery
    {
        [UsePaging]
        public IEnumerable<Product> GetProducts() => [];

        public PageConnection<Product> GetPaged()
            => new(Page<Product>.Empty);

        public StreamPageConnection<Product> GetStreamed()
            => new(StreamPage<Product>.Empty);
    }

    public sealed class Query
    {
        public StreamPageConnection<Product> GetStreamed()
            => new(StreamPage<Product>.Empty);
    }
}
