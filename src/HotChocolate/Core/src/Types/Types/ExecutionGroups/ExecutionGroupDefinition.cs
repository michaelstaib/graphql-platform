using System.Collections.Immutable;

namespace HotChocolate.Types;

/// <summary>
/// Defines the selections below a field that run one after the other instead of in parallel.
/// </summary>
public sealed class ExecutionGroupDefinition
{
    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionGroupDefinition"/>.
    /// </summary>
    /// <param name="entries">The members of the group.</param>
    public ExecutionGroupDefinition(IEnumerable<ExecutionGroupEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Entries = [.. entries];
    }

    /// <summary>
    /// Gets the members of the group in definition order.
    /// </summary>
    public ImmutableArray<ExecutionGroupEntry> Entries { get; }
}
