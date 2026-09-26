namespace GreenDonut.Data;

/// <summary>
/// Shared arithmetic for the last-page cursor helpers of <see cref="Page{T}"/> and
/// <see cref="StreamPage{T}"/>.
/// </summary>
internal static class LastPageCursorMath
{
    /// <summary>
    /// Computes the one-based number of the last page for a dataset.
    /// </summary>
    /// <param name="totalCount">
    /// The total number of items in the dataset.
    /// </param>
    /// <param name="requestedSize">
    /// The requested page size.
    /// </param>
    public static int GetLastPageNumber(int totalCount, int requestedSize)
        => Math.Max(1, (int)Math.Ceiling((double)totalCount / requestedSize));
}
