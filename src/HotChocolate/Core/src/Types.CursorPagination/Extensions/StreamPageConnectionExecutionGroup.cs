using System.Collections.Frozen;
using HotChocolate.Types.Descriptors.Configurations;

namespace HotChocolate.Types.Pagination;

/// <summary>
/// Defines the field paths of a stream page connection, the members of its execution group and the
/// data requirements each path has on the page.
/// </summary>
internal static class StreamPageConnectionExecutionGroup
{
    private const string PageInfoField = ConnectionType.Names.PageInfo;
    private const string ForwardCursors = "forwardCursors";
    private const string BackwardCursors = "backwardCursors";

    private static readonly ExecutionGroupEntry[] s_entries =
    [
        new(ConnectionType.Names.Edges, 0, isStreamable: true) { IsOptional = true },
        new(ConnectionType.Names.Nodes, 1, isStreamable: true) { IsOptional = true },
        new($"{PageInfoField}.{PageInfo.Names.HasNextPage}", int.MaxValue, isStreamable: false) { IsOptional = true },
        new($"{PageInfoField}.{PageInfo.Names.EndCursor}", int.MaxValue, isStreamable: false) { IsOptional = true },
        new($"{PageInfoField}.{ForwardCursors}", int.MaxValue, isStreamable: false) { IsOptional = true },
        new($"{PageInfoField}.{BackwardCursors}", int.MaxValue, isStreamable: false) { IsOptional = true }
    ];

    private static readonly string[] s_totalCountPaths =
    [
        ConnectionType.Names.TotalCount
    ];

    private static readonly string[] s_firstRowPaths =
    [
        $"{PageInfoField}.{PageInfo.Names.StartCursor}",
        $"{PageInfoField}.{PageInfo.Names.HasPreviousPage}"
    ];

    private static readonly FrozenDictionary<string, StreamPageDataRequirements> s_connectionRequirements =
        CreateRequirements(prefix: "");

    private static readonly FrozenDictionary<string, StreamPageDataRequirements> s_pageInfoRequirements =
        CreateRequirements(prefix: $"{PageInfoField}.");

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

    /// <summary>
    /// Gets the requirements of a connection field other than <c>pageInfo</c>.
    /// </summary>
    public static StreamPageDataRequirements GetConnectionRequirements(string fieldName)
        => s_connectionRequirements.GetValueOrDefault(fieldName, StreamPageDataRequirements.None);

    /// <summary>
    /// Gets the requirements of a <c>pageInfo</c> field.
    /// </summary>
    public static StreamPageDataRequirements GetPageInfoRequirements(string fieldName)
        => s_pageInfoRequirements.GetValueOrDefault(fieldName, StreamPageDataRequirements.None);

    private static FrozenDictionary<string, StreamPageDataRequirements> CreateRequirements(string prefix)
    {
        var requirements = new Dictionary<string, StreamPageDataRequirements>(StringComparer.Ordinal);

        foreach (var entry in s_entries)
        {
            Add(entry.Path, StreamPageDataRequirements.AllRows);
        }

        foreach (var path in s_totalCountPaths)
        {
            Add(path, StreamPageDataRequirements.TotalCount);
        }

        foreach (var path in s_firstRowPaths)
        {
            Add(path, StreamPageDataRequirements.FirstRow);
        }

        return requirements.ToFrozenDictionary(StringComparer.Ordinal);

        void Add(string path, StreamPageDataRequirements requirement)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                var fieldName = path[prefix.Length..];

                if (fieldName.IndexOf('.') < 0)
                {
                    requirements[fieldName] = requirement;
                }
            }
        }
    }
}
