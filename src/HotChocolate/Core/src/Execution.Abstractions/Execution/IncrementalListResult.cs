using System.Collections.Immutable;
using HotChocolate.Execution.Pipeline;

namespace HotChocolate.Execution;

/// <summary>
/// Represents an incremental result that delivers additional list items for a @stream directive.
/// </summary>
public sealed class IncrementalListResult : IIncrementalResult
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
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="items"/> does not have a formatter.
    /// </exception>
    public IncrementalListResult(
        int id,
        ImmutableList<IError>? errors,
        OperationResultData items)
    {
        if (items.Formatter is null)
        {
            throw ThrowHelper.IncrementalListResult_ItemsWithoutFormatter(nameof(items));
        }

        Id = id;
        Errors = errors ?? [];
        Items = items;
    }

    /// <inheritdoc />
    public int Id { get; }

    /// <inheritdoc />
    public ImmutableList<IError> Errors { get; }

    /// <summary>
    /// Gets the list items to append to the streamed list field.
    /// </summary>
    public OperationResultData Items { get; }
}
