using HotChocolate.Types.Descriptors.Configurations;

namespace HotChocolate.Types.Pagination;

/// <summary>
/// Defines the members of a stream page connection that can read the page beyond its first row.
/// </summary>
internal static class StreamPageConnectionExecutionGroup
{
    private static readonly ExecutionGroupEntry[] s_entries =
    [
        new("edges", 0, isStreamable: true) { IsOptional = true },
        new("nodes", 1, isStreamable: true) { IsOptional = true },
        new("pageInfo.hasNextPage", int.MaxValue, isStreamable: false) { IsOptional = true },
        new("pageInfo.endCursor", int.MaxValue, isStreamable: false) { IsOptional = true },
        new("pageInfo.forwardCursors", int.MaxValue, isStreamable: false) { IsOptional = true },
        new("pageInfo.backwardCursors", int.MaxValue, isStreamable: false) { IsOptional = true }
    ];

    /// <summary>
    /// Adds the members of the connection to the execution group of the field, keeping entries the field already declares.
    /// </summary>
    public static void Apply(ObjectFieldConfiguration field)
    {
        var group = field.ExecutionGroup ??= new ExecutionGroupConfiguration();

        foreach (var entry in s_entries)
        {
            if (!group.Entries.Exists(e => e.Path.Equals(entry.Path, StringComparison.Ordinal)))
            {
                group.Entries.Add(entry);
            }
        }
    }
}
