using GreenDonut.Data;

namespace HotChocolate.Types.Pagination;

/// <summary>
/// Information about pagination in a streaming connection.
/// </summary>
[GraphQLDescription(
    "Information about pagination in a connection.")]
[ConditionalShareable]
public abstract class StreamPageInfo
{
    /// <summary>
    /// Indicates whether more edges exist following
    /// the set defined by the clients arguments.
    /// </summary>
    [GraphQLDescription(
        "Indicates whether more edges exist following "
        + "the set defined by the clients arguments.")]
    public abstract ValueTask<bool> HasNextPage { get; }

    /// <summary>
    /// Indicates whether more edges exist prior
    /// the set defined by the clients arguments.
    /// </summary>
    [GraphQLDescription(
        "Indicates whether more edges exist prior "
        + "the set defined by the clients arguments.")]
    public abstract ValueTask<bool> HasPreviousPage { get; }

    /// <summary>
    /// When paginating backwards, the cursor to continue.
    /// </summary>
    [GraphQLDescription(
        "When paginating backwards, the cursor to continue.")]
    public abstract ValueTask<string?> StartCursor { get; }

    /// <summary>
    /// When paginating forwards, the cursor to continue.
    /// </summary>
    [GraphQLDescription(
        "When paginating forwards, the cursor to continue.")]
    public abstract ValueTask<string?> EndCursor { get; }

    /// <summary>
    /// A list of cursors to continue paginating forwards.
    /// </summary>
    [GraphQLDescription(
        "A list of cursors to continue paginating forwards.")]
    [GraphQLType<NonNullType<ListType<NonNullType<PageCursorType>>>>]
    public abstract ValueTask<IReadOnlyList<PageCursor>> ForwardCursors { get; }

    /// <summary>
    /// A list of cursors to continue paginating backwards.
    /// </summary>
    [GraphQLDescription(
        "A list of cursors to continue paginating backwards.")]
    [GraphQLType<NonNullType<ListType<NonNullType<PageCursorType>>>>]
    public abstract ValueTask<IReadOnlyList<PageCursor>> BackwardCursors { get; }
}

/// <summary>
/// Information about pagination in a streaming connection.
/// </summary>
/// <param name="page">
/// The page that contains the data.
/// </param>
/// <param name="maxRelativeCursorCount">
/// The maximum number of relative cursors to create.
/// </param>
/// <typeparam name="TNode">
/// The type of the node.
/// </typeparam>
public class StreamPageInfo<TNode>(StreamPage<TNode> page, int maxRelativeCursorCount) : StreamPageInfo
{
    /// <inheritdoc />
    public override ValueTask<bool> HasNextPage => page.HasNextPageAsync();

    /// <inheritdoc />
    public override ValueTask<bool> HasPreviousPage => page.HasPreviousPageAsync();

    /// <inheritdoc />
    public override ValueTask<string?> StartCursor => page.CreateStartCursorAsync();

    /// <inheritdoc />
    public override ValueTask<string?> EndCursor => page.CreateEndCursorAsync();

    /// <inheritdoc />
    public override ValueTask<IReadOnlyList<PageCursor>> ForwardCursors => CreateForwardCursorsAsync();

    /// <inheritdoc />
    public override ValueTask<IReadOnlyList<PageCursor>> BackwardCursors => CreateBackwardCursorsAsync();

    private async ValueTask<IReadOnlyList<PageCursor>> CreateForwardCursorsAsync()
        => await page.CreateRelativeForwardCursorsAsync(maxRelativeCursorCount).ConfigureAwait(false);

    private async ValueTask<IReadOnlyList<PageCursor>> CreateBackwardCursorsAsync()
        => await page.CreateRelativeBackwardCursorsAsync(maxRelativeCursorCount).ConfigureAwait(false);
}
