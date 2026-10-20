using System.Collections.Immutable;

namespace HotChocolate.Types;

/// <summary>
/// Describes one member of an execution group by the selection path relative to the group root field.
/// </summary>
public sealed class ExecutionGroupEntry
{
    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionGroupEntry"/>.
    /// </summary>
    /// <param name="path">
    /// The dot separated field names relative to the group root, for example <c>pageInfo.hasNextPage</c>.
    /// </param>
    /// <param name="priority">
    /// The position of the member in the sequence. Lower values run first.
    /// </param>
    /// <param name="isStreamable">
    /// Defines whether the member can be consumed incrementally.
    /// </param>
    public ExecutionGroupEntry(string path, int priority, bool isStreamable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;
        Priority = priority;
        IsStreamable = isStreamable;
        Segments = [.. path.Split('.')];
    }

    /// <summary>
    /// Gets the dot separated field names relative to the group root.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the position of the member in the sequence. Lower values run first.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets a value indicating whether the member can be consumed incrementally.
    /// </summary>
    public bool IsStreamable { get; }

    internal ImmutableArray<string> Segments { get; }
}
