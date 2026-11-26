namespace HotChocolate.Types.Pagination;

internal interface IStreamPageConnection
{
    /// <summary>
    /// Loads the data the given requirements describe and at least <paramref name="rowCount"/> leading rows,
    /// so the page answers them from memory.
    /// </summary>
    ValueTask LoadRequirementsAsync(
        StreamPageDataRequirements requirements,
        int rowCount,
        CancellationToken cancellationToken = default);

    ValueTask DisposeAsync();
}
