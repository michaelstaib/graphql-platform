using System.Collections.Immutable;

namespace HotChocolate.Execution;

/// <summary>
/// Represents an incremental result that delivers additional list items for a @stream directive.
/// </summary>
public sealed class IncrementalListResult : IIncrementalListResult
{
    /// <summary>
    /// Initializes a new instance of <see cref="IncrementalListResult"/>.
    /// </summary>
    /// <param name="id">
    /// The unique identifier that correlates this result with its pending entry.
    /// </param>
    /// <param name="errors">
    /// The GraphQL errors that occurred while resolving the streamed items.
    /// </param>
    /// <param name="items">
    /// The list items to append to the streamed list field.
    /// </param>
    public IncrementalListResult(
        int id,
        ImmutableList<IError>? errors,
        IReadOnlyList<object?> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Id = id;
        Errors = errors ?? [];
        Items = items;
    }

    /// <inheritdoc />
    public int Id { get; }

    /// <inheritdoc />
    public ImmutableList<IError> Errors { get; }

    /// <inheritdoc />
    public IReadOnlyList<object?> Items { get; }
}
