using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Walks an operation AST once and produces the <see cref="DeliveryGroup"/> tree
/// for all <c>@defer</c> occurrences. The resulting mapping from
/// <see cref="InlineFragmentNode"/> instance to <see cref="DeliveryGroup"/> is the
/// single source of truth for defer topology, consumed by the planner pipeline
/// stages (rewriter, compiler) so they do not perform parallel AST walks with
/// divergent state.
/// </summary>
internal static class DeferPartitioner
{
    /// <summary>
    /// Walks <paramref name="operation"/> and produces the complete
    /// <see cref="DeliveryGroup"/> tree for every <c>... @defer</c> inline
    /// fragment encountered. Defer conditions are registered into
    /// <paramref name="deferConditions"/> (passed in rather than owned so the
    /// caller can share one collection with the operation being compiled).
    /// </summary>
    public static DeferPartitioningResult Partition(
        OperationDefinitionNode operation,
        DeferConditionCollection deferConditions)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(deferConditions);

        var occurrences = new List<Occurrence>();

        Walk(
            operation.SelectionSet.Selections,
            parent: null,
            currentPath: SelectionPath.Root,
            deferConditions,
            occurrences);

        // The lowering numbers the groups. A document without numbers, or with numbers that disagree, is numbered
        // in declaration order.
        var numbered = HasGroupNumbers(occurrences);
        var numberingError = numbered ? FindNumberingError(occurrences) : null;
        var groups = numbered && numberingError is null
            ? CreateNumberedGroups(occurrences)
            : CreatePositionalGroups(occurrences);

        var byFragment = new Dictionary<InlineFragmentNode, DeliveryGroup>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < occurrences.Count; i++)
        {
            byFragment[occurrences[i].Fragment] = groups.ByOccurrence[i];
        }

        return new DeferPartitioningResult(groups.All, byFragment, numberingError);
    }

    private static void Walk(
        IReadOnlyList<ISelectionNode> selections,
        int? parent,
        SelectionPath currentPath,
        DeferConditionCollection deferConditions,
        List<Occurrence> occurrences)
    {
        for (var i = 0; i < selections.Count; i++)
        {
            var selection = selections[i];

            if (selection is FieldNode field)
            {
                if (field.SelectionSet is { } sub)
                {
                    var responseName = field.Alias?.Value ?? field.Name.Value;
                    var childPath = currentPath.AppendField(responseName);
                    Walk(sub.Selections, parent, childPath, deferConditions, occurrences);
                }

                continue;
            }

            if (selection is InlineFragmentNode inline)
            {
                var nested = parent;

                if (DeferCondition.TryCreate(inline, out var deferCondition))
                {
                    deferConditions.Add(deferCondition);
                    var deferIndex = deferConditions.IndexOf(deferCondition);
                    var internalArguments = GetInternalArguments(inline);

                    occurrences.Add(
                        new Occurrence(
                            inline,
                            parent,
                            currentPath,
                            GetDeferLabel(inline),
                            GetDeferIfVariable(inline),
                            deferIndex,
                            internalArguments.Group,
                            internalArguments.Parent,
                            internalArguments.Levels));
                    nested = occurrences.Count - 1;
                }

                Walk(inline.SelectionSet.Selections, nested, currentPath, deferConditions, occurrences);
                continue;
            }

            // FragmentSpreadNode / other shapes are not expected at planner input
            // (document rewriter inlines spreads); fall through silently.
        }
    }

    private static bool HasGroupNumbers(List<Occurrence> occurrences)
    {
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Group is not null || occurrence.Parent is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the reason why the group numbers of the fragments disagree, or <c>null</c> when they agree.
    /// </summary>
    private static string? FindNumberingError(List<Occurrence> occurrences)
    {
        var parents = new Dictionary<int, int>();

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Group is not { } number || occurrence.Parent is not { } parent)
            {
                return "A @defer fragment carries no group number while another one does.";
            }

            if (parents.TryGetValue(number, out var known) && known != parent)
            {
                return $"The fragments of delivery group {number} name different parents.";
            }

            if (parent < -1 || parent >= number)
            {
                return $"The parent {parent} of delivery group {number} is not a lower group number.";
            }

            if (occurrence.Levels < 0 || occurrence.Levels > occurrence.Path.Length)
            {
                return $"A fragment of delivery group {number} spans {occurrence.Levels} field levels "
                    + $"below a path of {occurrence.Path.Length}.";
            }

            parents[number] = parent;
        }

        foreach (var (number, parent) in parents)
        {
            if (parent >= 0 && !parents.ContainsKey(parent))
            {
                return $"The parent {parent} of delivery group {number} is not the number of a group.";
            }
        }

        return null;
    }

    /// <summary>
    /// Creates one <see cref="DeliveryGroup"/> per group number, in the order of the numbers. The path of a group
    /// is the path of its fragment without the field levels of a part, and its parent is the group of the parent
    /// number.
    /// </summary>
    private static Groups CreateNumberedGroups(List<Occurrence> occurrences)
    {
        var numbers = occurrences.Select(static o => o.Group!.Value).Distinct().Order().ToArray();
        var first = new Occurrence?[numbers.Length];

        // A group is created from the fragment at its place, which has no field levels. A group that has
        // only parts is created from its first part.
        foreach (var occurrence in occurrences)
        {
            var rank = Array.BinarySearch(numbers, occurrence.Group!.Value);

            if (first[rank] is null || (first[rank]!.Levels > 0 && occurrence.Levels == 0))
            {
                first[rank] = occurrence;
            }
        }

        var all = ImmutableArray.CreateBuilder<DeliveryGroup>(first.Length);

        for (var rank = 0; rank < first.Length; rank++)
        {
            var occurrence = first[rank]!;
            var path = occurrence.Path.Slice(occurrence.Path.Length - occurrence.Levels);
            var parentNumber = occurrence.Parent!.Value;
            var parent = parentNumber >= 0 ? all[Array.BinarySearch(numbers, parentNumber)] : null;

            all.Add(
                new DeliveryGroup(occurrence.Label, parent, occurrence.DeferConditionIndex)
                {
                    Id = rank,
                    Path = path,
                    IfVariable = occurrence.IfVariable
                });
        }

        var byOccurrence = new DeliveryGroup[occurrences.Count];

        for (var i = 0; i < occurrences.Count; i++)
        {
            byOccurrence[i] = all[Array.BinarySearch(numbers, occurrences[i].Group!.Value)];
        }

        return new Groups(all.MoveToImmutable(), byOccurrence);
    }

    /// <summary>
    /// Creates one <see cref="DeliveryGroup"/> per fragment, numbered in declaration order, with the group of the
    /// enclosing fragment as parent.
    /// </summary>
    private static Groups CreatePositionalGroups(List<Occurrence> occurrences)
    {
        var all = ImmutableArray.CreateBuilder<DeliveryGroup>(occurrences.Count);

        for (var i = 0; i < occurrences.Count; i++)
        {
            var occurrence = occurrences[i];
            var parent = occurrence.EnclosingOccurrence is { } enclosing ? all[enclosing] : null;

            all.Add(
                new DeliveryGroup(occurrence.Label, parent, occurrence.DeferConditionIndex)
                {
                    Id = i,
                    Path = occurrence.Path,
                    IfVariable = occurrence.IfVariable
                });
        }

        var byOccurrence = all.ToArray();
        return new Groups(all.MoveToImmutable(), byOccurrence);
    }

    private static (int? Group, int? Parent, int Levels) GetInternalArguments(InlineFragmentNode node)
    {
        int? group = null;
        int? parent = null;
        var levels = 0;

        foreach (var directive in node.Directives)
        {
            if (!directive.Name.Value.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var argument in directive.Arguments)
            {
                if (argument.Value is not IntValueNode value)
                {
                    continue;
                }

                switch (argument.Name.Value)
                {
                    case DocumentRewriter.DeferArguments.Group:
                        group = value.ToInt32();
                        break;

                    case DocumentRewriter.DeferArguments.Parent:
                        parent = value.ToInt32();
                        break;

                    case DocumentRewriter.DeferArguments.Levels:
                        levels = value.ToInt32();
                        break;
                }
            }
        }

        return (group, parent, levels);
    }

    private sealed record Occurrence(
        InlineFragmentNode Fragment,
        int? EnclosingOccurrence,
        SelectionPath Path,
        string? Label,
        string? IfVariable,
        int DeferConditionIndex,
        int? Group,
        int? Parent,
        int Levels);

    private sealed record Groups(ImmutableArray<DeliveryGroup> All, DeliveryGroup[] ByOccurrence);

    private static string? GetDeferLabel(InlineFragmentNode node)
    {
        for (var i = 0; i < node.Directives.Count; i++)
        {
            var directive = node.Directives[i];

            if (!directive.Name.Value.Equals(
                DirectiveNames.Defer.Name,
                StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = 0; j < directive.Arguments.Count; j++)
            {
                var arg = directive.Arguments[j];

                if (arg.Name.Value.Equals("label", StringComparison.Ordinal)
                    && arg.Value is StringValueNode stringValue)
                {
                    return stringValue.Value;
                }
            }
        }

        return null;
    }

    private static string? GetDeferIfVariable(InlineFragmentNode node)
    {
        for (var i = 0; i < node.Directives.Count; i++)
        {
            var directive = node.Directives[i];

            if (!directive.Name.Value.Equals(
                DirectiveNames.Defer.Name,
                StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = 0; j < directive.Arguments.Count; j++)
            {
                var arg = directive.Arguments[j];

                if (arg.Name.Value.Equals("if", StringComparison.Ordinal)
                    && arg.Value is VariableNode variable)
                {
                    return variable.Name.Value;
                }
            }
        }

        return null;
    }
}
