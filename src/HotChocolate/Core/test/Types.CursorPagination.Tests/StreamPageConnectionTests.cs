using GreenDonut.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HotChocolate.Types.Pagination;

public class StreamPageConnectionTests
{
    [Fact]
    public async Task ImplicitConversion_Should_WrapPage_When_ConvertingStreamPageToConnection()
    {
        // arrange
        await using var database = await TestDatabase.CreateAsync(5);
        var page = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 2, includeTotalCount: true),
            cancellationToken: TestContext.Current.CancellationToken);

        // act
        StreamPageConnection<Item> connection = page;

        // assert
        Assert.Same(page, connection.Nodes);
        Assert.Equal(5, await connection.TotalCount);
    }

    [Fact]
    public void ImplicitConversion_Should_ThrowArgumentNullException_When_PageIsNull()
    {
        // arrange
        StreamPage<string>? page = null;

        // act
        StreamPageConnection<string> Convert() => page!;

        // assert
        Assert.Throws<ArgumentNullException>(Convert);
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentOutOfRangeException_When_MaxRelativeCursorCountIsNegative()
    {
        // arrange
        var page = StreamPage<string>.Empty;

        // act
        StreamPageConnection<string> Create() => new(page, maxRelativeCursorCount: -1);

        // assert
        Assert.Throws<ArgumentOutOfRangeException>(Create);
    }

    [Fact]
    public async Task Edges_Should_PreserveEntryCursors_When_PageIsEnumerated()
    {
        // arrange
        await using var database = await TestDatabase.CreateAsync(5);
        var page = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 3),
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = new StreamPageConnection<Item>(page);

        // act
        List<object> edges = [];
        await foreach (var edge in connection.Edges!.WithCancellation(TestContext.Current.CancellationToken))
        {
            edges.Add(new { edge.Node.Id, edge.Cursor });
        }

        // assert
        edges.MatchInlineSnapshot(
            """
            [
              {
                "Id": 1,
                "Cursor": "e30x"
              },
              {
                "Id": 2,
                "Cursor": "e30y"
              },
              {
                "Id": 3,
                "Cursor": "e30z"
              }
            ]
            """);
    }

    [Fact]
    public async Task Edges_Should_ReplayAllEdges_When_EnumeratedTwice()
    {
        // arrange
        await using var database = await TestDatabase.CreateAsync(5);
        var page = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 3),
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = new StreamPageConnection<Item>(page);

        // act
        var first = await CollectIdsAsync(connection);
        var second = await CollectIdsAsync(connection);

        // assert
        Assert.Equal([1, 2, 3], first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PageInfo_Should_ResolveFlagsAndCursors_When_RelativeCursorsAreEnabled()
    {
        // arrange
        await using var database = await TestDatabase.CreateAsync(10);
        var page = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 2, includeTotalCount: true) { EnableRelativeCursors = true },
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = new StreamPageConnection<Item>(page, maxRelativeCursorCount: 2);

        // act
        var pageInfo = connection.PageInfo;
        var snapshot = new
        {
            HasNextPage = await pageInfo.HasNextPage,
            HasPreviousPage = await pageInfo.HasPreviousPage,
            StartCursor = await pageInfo.StartCursor,
            EndCursor = await pageInfo.EndCursor,
            ForwardCursors = await pageInfo.ForwardCursors,
            BackwardCursors = await pageInfo.BackwardCursors
        };

        // assert
        snapshot.MatchInlineSnapshot(
            """
            {
              "HasNextPage": true,
              "HasPreviousPage": false,
              "StartCursor": "e30x",
              "EndCursor": "e30y",
              "ForwardCursors": [
                {
                  "Cursor": "ezB8MXwxMH0y",
                  "Page": 2
                },
                {
                  "Cursor": "ezF8MXwxMH0y",
                  "Page": 3
                }
              ],
              "BackwardCursors": []
            }
            """);
    }

    [Fact]
    public async Task PageInfo_Should_ResolveBackwardCursors_When_PageIsNotTheFirstPage()
    {
        // arrange
        await using var database = await TestDatabase.CreateAsync(10);
        var firstPage = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 2, includeTotalCount: true) { EnableRelativeCursors = true },
            cancellationToken: TestContext.Current.CancellationToken);
        var lastEntry = await GetLastEntryAndDisposeAsync(firstPage);
        var cursor = firstPage.CreateCursor(lastEntry, 0);
        var page = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 2, after: cursor) { EnableRelativeCursors = true },
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = new StreamPageConnection<Item>(page);

        // act
        var pageInfo = connection.PageInfo;
        var snapshot = new
        {
            HasNextPage = await pageInfo.HasNextPage,
            HasPreviousPage = await pageInfo.HasPreviousPage,
            BackwardCursors = await pageInfo.BackwardCursors
        };

        // assert
        snapshot.MatchInlineSnapshot(
            """
            {
              "HasNextPage": true,
              "HasPreviousPage": true,
              "BackwardCursors": [
                {
                  "Cursor": "ezB8MnwxMH0z",
                  "Page": 1
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task TotalCount_Should_BeNull_When_CountWasNotRequested()
    {
        // arrange
        await using var database = await TestDatabase.CreateAsync(5);
        var page = await database.Query.ToStreamPageAsync(
            new PagingArguments(first: 2),
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = new StreamPageConnection<Item>(page);

        // act
        var totalCount = await connection.TotalCount;

        // assert
        Assert.Null(totalCount);
    }

    [Fact]
    public async Task Connection_Should_BeEmpty_When_PageIsEmpty()
    {
        // arrange
        StreamPageConnection<string> connection = StreamPage<string>.Empty;

        // act
        List<object?> edges = [];
        await foreach (var edge in connection.Edges!.WithCancellation(TestContext.Current.CancellationToken))
        {
            edges.Add(edge);
        }

        var pageInfo = connection.PageInfo;
        var snapshot = new
        {
            Edges = edges,
            TotalCount = await connection.TotalCount,
            HasNextPage = await pageInfo.HasNextPage,
            HasPreviousPage = await pageInfo.HasPreviousPage,
            StartCursor = await pageInfo.StartCursor,
            EndCursor = await pageInfo.EndCursor,
            ForwardCursors = await pageInfo.ForwardCursors,
            BackwardCursors = await pageInfo.BackwardCursors
        };

        // assert
        snapshot.MatchInlineSnapshot(
            """
            {
              "Edges": [],
              "TotalCount": 0,
              "HasNextPage": false,
              "HasPreviousPage": false,
              "StartCursor": null,
              "EndCursor": null,
              "ForwardCursors": [],
              "BackwardCursors": []
            }
            """);
    }

    private static async Task<PageEntry<Item>> GetLastEntryAndDisposeAsync(StreamPage<Item> page)
    {
        PageEntry<Item>? last = null;

        await foreach (var entry in page.GetEntriesAsync(TestContext.Current.CancellationToken))
        {
            last = entry;
        }

        await page.DisposeAsync();

        return last!.Value;
    }

    private static async Task<List<int>> CollectIdsAsync(StreamPageConnection<Item> connection)
    {
        List<int> ids = [];

        await foreach (var edge in connection.Edges!.WithCancellation(TestContext.Current.CancellationToken))
        {
            ids.Add(edge.Node.Id);
        }

        return ids;
    }

    public sealed class Item
    {
        public int Id { get; set; }
    }

    private sealed class PagingContext(DbContextOptions<PagingContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly PagingContext _context;

        private TestDatabase(SqliteConnection connection, PagingContext context)
        {
            _connection = connection;
            _context = context;
        }

        public IQueryable<Item> Query => _context.Items.OrderBy(t => t.Id);

        public static async Task<TestDatabase> CreateAsync(int itemCount)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<PagingContext>()
                .UseSqlite(connection)
                .Options;
            var context = new PagingContext(options);
            await context.Database.EnsureCreatedAsync(cancellationToken);

            for (var i = 1; i <= itemCount; i++)
            {
                context.Items.Add(new Item { Id = i });
            }

            await context.SaveChangesAsync(cancellationToken);

            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
