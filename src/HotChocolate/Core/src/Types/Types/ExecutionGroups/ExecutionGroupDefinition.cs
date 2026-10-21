using System.Collections.Immutable;

namespace HotChocolate.Types;

/// <summary>
/// Defines the selections below a field that run one after the other instead of in parallel.
/// A member runs after its predecessor has completed its own resolver task. Member paths cannot pass through
/// list fields, abstract types, other group roots or batch resolver fields, and members cannot be serial.
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
    /// Gets the members of the group in definition order. Members with equal priority run in definition order,
    /// then in selection order.
    /// </summary>
    public ImmutableArray<ExecutionGroupEntry> Entries { get; }
}
