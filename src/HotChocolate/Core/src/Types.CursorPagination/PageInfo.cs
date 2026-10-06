using GreenDonut.Data;

namespace HotChocolate.Types.Pagination;

/// <summary>
/// Information about pagination in a connection.
/// </summary>
[GraphQLDescription(
    "Information about pagination in a connection.")]
[ConditionalShareable]
public abstract class PageInfo : IPageInfo
{
    /// <summary>
    /// Indicates whether more edges exist following
    /// the set defined by the clients arguments.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that cancels the operation.
    /// </param>
    [GraphQLDescription(
        "Indicates whether more edges exist following "
        + "the set defined by the clients arguments.")]
    public abstract ValueTask<bool> HasNextPageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether more edges exist prior
    /// the set defined by the clients arguments.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that cancels the operation.
    /// </param>
    [GraphQLDescription(
        "Indicates whether more edges exist prior "
        + "the set defined by the clients arguments.")]
    public abstract ValueTask<bool> HasPreviousPageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// When paginating backwards, the cursor to continue.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that cancels the operation.
    /// </param>
    [GraphQLDescription(
        "When paginating backwards, the cursor to continue.")]
    public abstract ValueTask<string?> GetStartCursorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// When paginating forwards, the cursor to continue.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that cancels the operation.
    /// </param>
    [GraphQLDescription(
        "When paginating forwards, the cursor to continue.")]
    public abstract ValueTask<string?> GetEndCursorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A list of cursors to continue paginating forwards.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that cancels the operation.
    /// </param>
    [GraphQLDescription(
        "A list of cursors to continue paginating forwards.")]
    [GraphQLType<NonNullType<ListType<NonNullType<PageCursorType>>>>]
    public abstract ValueTask<IReadOnlyList<PageCursor>> GetForwardCursorsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A list of cursors to continue paginating backwards.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token that cancels the operation.
    /// </param>
    [GraphQLDescription(
        "A list of cursors to continue paginating backwards.")]
    [GraphQLType<NonNullType<ListType<NonNullType<PageCursorType>>>>]
    public abstract ValueTask<IReadOnlyList<PageCursor>> GetBackwardCursorsAsync(
        CancellationToken cancellationToken = default);
}
