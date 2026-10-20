namespace HotChocolate.Types.Pagination;

internal interface IStreamPageConnection
{
    /// <summary>
    /// Reads from the source of the page whatever the given reads need.
    /// </summary>
    ValueTask PreReadAsync(StreamPageReads reads, CancellationToken cancellationToken = default);

    ValueTask DisposeAsync();
}
