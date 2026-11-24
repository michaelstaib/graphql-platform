using System.Collections.Frozen;
using System.Collections.Immutable;
using HotChocolate.Types;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// The sequence of an execution group resolved against the selections of one operation.
/// </summary>
internal sealed class ExecutionGroupPlan
{
    private readonly FrozenDictionary<int, ExecutionGroupNode> _nodes;
    private readonly bool _hasStreams;

    private ExecutionGroupPlan(
        ImmutableArray<ExecutionGroupSlot> slots,
        FrozenDictionary<int, ExecutionGroupNode> nodes,
        bool hasStreams)
    {
        Slots = slots;
        _nodes = nodes;
        _hasStreams = hasStreams;
    }

    /// <summary>
    /// Gets the members of the group in the order in which they run.
    /// </summary>
    public ImmutableArray<ExecutionGroupSlot> Slots { get; }

    /// <summary>
    /// Gets the node of the selection with the specified id.
    /// </summary>
    public bool TryGetNode(int selectionId, out ExecutionGroupNode node)
        => _nodes.TryGetValue(selectionId, out node);

    /// <summary>
    /// Gets a value indicating whether the streamed members of the group stream for a request. Streams are
    /// ignored when a selected streamable member is not streamed, or when a selected member that is not
    /// streamable is delivered in the payload of the group root.
    /// </summary>
    /// <param name="includeFlags">The include condition flags of the request.</param>
    /// <param name="deferFlags">The defer condition flags of the request.</param>
    /// <param name="rootDeferUsage">The primary defer usage of the group root, or <c>null</c> if it is not deferred.</param>
    /// <param name="variables">The variable values of the request.</param>
    public bool HonorsStreams(
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        DeferUsage? rootDeferUsage,
        IVariableValueCollection variables)
    {
        if (!_hasStreams)
        {
            return false;
        }

        var hasStreamedMember = false;
        var hasUnstreamedMember = false;
        var hasNonStreamableMemberWithRoot = false;

        foreach (var slot in Slots)
        {
            var selection = slot.Selection;

            if (!selection.IsIncluded(includeFlags))
            {
                continue;
            }

            if (!slot.IsStreamable)
            {
                hasNonStreamableMemberWithRoot |= selection.GetPrimaryDeferUsage(deferFlags) == rootDeferUsage;
            }
            else if (selection.IsStreamable && selection.StreamArguments is { } stream && stream.IsEnabled(variables))
            {
                hasStreamedMember = true;
            }
            else
            {
                hasUnstreamedMember = true;
            }
        }

        return hasStreamedMember && !hasUnstreamedMember && !hasNonStreamableMemberWithRoot;
    }

    /// <summary>
    /// Resolves the entries of <paramref name="group"/> against the selection set of a group root.
    /// Members are ordered by ascending priority, then by entry order, then by selection order.
    /// </summary>
    public static ExecutionGroupPlan Create(
        Operation operation,
        SelectionSet rootSelectionSet,
        ExecutionGroup group)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(rootSelectionSet);
        ArgumentNullException.ThrowIfNull(group);

        var found = new List<Candidate>();
        var ancestors = new List<Selection>();

        foreach (var entry in group.Entries)
        {
            Collect(operation, rootSelectionSet, entry, 0, ancestors, found);
        }

        var ordered = found
            .Select((candidate, index) => (candidate, index))
            .OrderBy(t => t.candidate.Entry.Priority)
            .ThenBy(t => t.index)
            .Select(t => t.candidate)
            .ToArray();

        var slots = ImmutableArray.CreateBuilder<ExecutionGroupSlot>(ordered.Length);
        var builders = new Dictionary<int, NodeBuilder>();
        var hasStreams = false;

        for (var i = 0; i < ordered.Length; i++)
        {
            var candidate = ordered[i];

            // the group decides which of its members honor the @stream directive.
            candidate.Selection.SetStreamable(candidate.Entry.IsStreamable);
            hasStreams |= candidate.Selection.IsStreamable && candidate.Selection.StreamArguments is not null;

            slots.Add(new ExecutionGroupSlot(
                candidate.Selection,
                candidate.Entry.Priority,
                candidate.Entry.IsStreamable));

            GetBuilder(builders, candidate.Selection).SlotIndex = i;

            foreach (var ancestor in candidate.Ancestors)
            {
                GetBuilder(builders, ancestor).SlotsBelow.Add(i);
            }
        }

        // schema validation rejects serial and batch members, this only guards plans that bypass it.
        foreach (var (_, builder) in builders)
        {
            if (builder.Selection.Strategy is SelectionExecutionStrategy.Batch)
            {
                throw ThrowHelper.ExecutionGroup_BatchSelectionNotSupported(builder.Selection);
            }

            if (builder.Selection.Strategy is SelectionExecutionStrategy.Serial)
            {
                throw ThrowHelper.ExecutionGroup_SerialSelectionNotSupported(builder.Selection);
            }
        }

        var nodes = new Dictionary<int, ExecutionGroupNode>(builders.Count);

        foreach (var (_, builder) in builders)
        {
            nodes.Add(
                builder.Selection.Id,
                new ExecutionGroupNode(builder.SlotIndex, [.. builder.SlotsBelow]));
        }

        return new ExecutionGroupPlan(
            slots.MoveToImmutable(),
            nodes.ToFrozenDictionary(),
            hasStreams && operation.HasIncrementalParts);
    }

    private static void Collect(
        Operation operation,
        SelectionSet selectionSet,
        ExecutionGroupEntry entry,
        int depth,
        List<Selection> ancestors,
        List<Candidate> found)
    {
        var segment = entry.Segments[depth];
        var isLast = depth == entry.Segments.Length - 1;

        foreach (var selection in selectionSet.Selections)
        {
            if (!selection.Field.Name.Equals(segment, StringComparison.Ordinal))
            {
                continue;
            }

            if (isLast)
            {
                found.Add(new Candidate(selection, entry, [.. ancestors]));
                continue;
            }

            if (selection.IsLeaf || selection.Field.Type.NamedType() is not ObjectType objectType)
            {
                continue;
            }

            ancestors.Add(selection);
            Collect(operation, operation.GetSelectionSet(selection, objectType), entry, depth + 1, ancestors, found);
            ancestors.RemoveAt(ancestors.Count - 1);
        }
    }

    private static NodeBuilder GetBuilder(Dictionary<int, NodeBuilder> builders, Selection selection)
    {
        if (!builders.TryGetValue(selection.Id, out var builder))
        {
            builder = new NodeBuilder(selection);
            builders.Add(selection.Id, builder);
        }

        return builder;
    }

    private sealed record Candidate(Selection Selection, ExecutionGroupEntry Entry, Selection[] Ancestors);

    private sealed class NodeBuilder(Selection selection)
    {
        public Selection Selection { get; } = selection;

        public int SlotIndex { get; set; } = -1;

        public List<int> SlotsBelow { get; } = [];
    }
}
