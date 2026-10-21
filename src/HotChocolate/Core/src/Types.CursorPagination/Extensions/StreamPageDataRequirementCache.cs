using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace HotChocolate.Types.Pagination;

/// <summary>
/// Holds the data requirements of one connection selection per combination of the request
/// conditions that influence them.
/// </summary>
internal sealed class StreamPageDataRequirementCache
{
    private const int MaxEntries = 16;
    private const int MaxStreamVariables = 64;

    private readonly string[] _streamVariables;
    private Entry[] _entries = [];

    private StreamPageDataRequirementCache(string[] streamVariables)
    {
        _streamVariables = streamVariables;
    }

    public static StreamPageDataRequirementCache Create(Selection selection, ObjectType connectionType)
    {
        List<string> streamVariables = [];

        foreach (var child in selection.GetSelectionSet(connectionType).Selections)
        {
            CollectStreamVariables(child, streamVariables);

            if (child.Field.Name.Equals(ConnectionType.Names.PageInfo, StringComparison.Ordinal)
                && child.Type.NamedType() is ObjectType pageInfoType)
            {
                foreach (var pageInfoChild in child.GetSelectionSet(pageInfoType).Selections)
                {
                    CollectStreamVariables(pageInfoChild, streamVariables);
                }
            }
        }

        return new StreamPageDataRequirementCache([.. streamVariables]);
    }

    public StreamPageDataRequirements GetOrAnalyze(IResolverContext context, ObjectType connectionType)
    {
        var includeFlags = context.IncludeConditionFlags;
        var deferFlags = context.Operation.CreateDeferConditionFlags(context.Variables);

        if (_streamVariables.Length > MaxStreamVariables)
        {
            return StreamPageDataRequirementAnalyzer.Compute(context, connectionType, deferFlags);
        }

        var streamFlags = 0ul;

        for (var i = 0; i < _streamVariables.Length; i++)
        {
            if (StreamPageDataRequirementAnalyzer.IsStreamEnabled(_streamVariables[i], context.Variables))
            {
                streamFlags |= 1ul << i;
            }
        }

        foreach (var entry in Volatile.Read(ref _entries))
        {
            if (entry.Matches(includeFlags, deferFlags, streamFlags))
            {
                return entry.Requirements;
            }
        }

        var requirements = StreamPageDataRequirementAnalyzer.Compute(context, connectionType, deferFlags);
        Add(new Entry(includeFlags, deferFlags, streamFlags, requirements));

        return requirements;
    }

    private void Add(Entry entry)
    {
        while (true)
        {
            var current = Volatile.Read(ref _entries);

            if (current.Length >= MaxEntries)
            {
                return;
            }

            foreach (var existing in current)
            {
                if (existing.Matches(entry.IncludeFlags, entry.DeferFlags, entry.StreamFlags))
                {
                    return;
                }
            }

            var next = new Entry[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = entry;

            if (Interlocked.CompareExchange(ref _entries, next, current) == current)
            {
                return;
            }
        }
    }

    private static void CollectStreamVariables(Selection selection, List<string> streamVariables)
    {
        foreach (var syntaxNode in selection.SyntaxNodes)
        {
            foreach (var directive in syntaxNode.Node.Directives)
            {
                if (directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal)
                    && directive.GetArgumentValue(DirectiveNames.Stream.Arguments.If) is VariableNode variable
                    && !streamVariables.Contains(variable.Name.Value, StringComparer.Ordinal))
                {
                    streamVariables.Add(variable.Name.Value);
                }
            }
        }
    }

    private sealed class Entry(
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        ulong streamFlags,
        StreamPageDataRequirements requirements)
    {
        public ConditionFlags IncludeFlags { get; } = includeFlags;

        public ConditionFlags DeferFlags { get; } = deferFlags;

        public ulong StreamFlags { get; } = streamFlags;

        public StreamPageDataRequirements Requirements { get; } = requirements;

        public bool Matches(ConditionFlags includeFlags, ConditionFlags deferFlags, ulong streamFlags)
            => StreamFlags == streamFlags
                && FlagsEqual(IncludeFlags, includeFlags)
                && FlagsEqual(DeferFlags, deferFlags);

        private static bool FlagsEqual(ConditionFlags left, ConditionFlags right)
            => left.Word0 == right.Word0
                && (ReferenceEquals(left.Overflow, right.Overflow)
                    || (left.Overflow is not null
                        && right.Overflow is not null
                        && left.Overflow.AsSpan().SequenceEqual(right.Overflow)));
    }
}
