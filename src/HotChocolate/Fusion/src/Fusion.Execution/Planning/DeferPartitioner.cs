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

        // The lowering numbers the groups. A document without numbers is numbered in declaration order.
        var groups = HasGroupNumbers(occurrences)
            ? CreateNumberedGroups(occurrences)
            : CreatePositionalGroups(occurrences);

        var byFragment = new Dictionary<InlineFragmentNode, DeliveryGroup>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < occurrences.Count; i++)
        {
            byFragment[occurrences[i].Fragment] = groups.ByOccurrence[i];
        }

        return new DeferPartitioningResult(groups.All, byFragment);
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
        if (occurrences.Count == 0)
        {
            return false;
        }

        var numbers = new HashSet<int>();

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Group is not { } group || occurrence.Parent is null)
            {
                return false;
            }

            numbers.Add(group);
        }

        // The numbers are dense, so a number is the index of its group.
        for (var i = 0; i < numbers.Count; i++)
        {
            if (!numbers.Contains(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates one <see cref="DeliveryGroup"/> per group number. The path of a group is the path of its fragment
    /// without the field levels of a part, and its parent is the group of the parent number.
    /// </summary>
    private static Groups CreateNumberedGroups(List<Occurrence> occurrences)
    {
        var first = new Occurrence?[occurrences.Max(static o => o.Group!.Value) + 1];

        // A group is created from the fragment at its place, which has no field levels. A group that has
        // only parts is created from its first part.
        foreach (var occurrence in occurrences)
        {
            var number = occurrence.Group!.Value;

            if (first[number] is null || (first[number]!.Levels > 0 && occurrence.Levels == 0))
            {
                first[number] = occurrence;
            }
        }

        var all = ImmutableArray.CreateBuilder<DeliveryGroup>(first.Length);

        for (var number = 0; number < first.Length; number++)
        {
            var occurrence = first[number]!;
            var path = occurrence.Path.Slice(Math.Max(0, occurrence.Path.Length - occurrence.Levels));
            var parentNumber = occurrence.Parent!.Value;
            var parent = parentNumber >= 0 && parentNumber < number ? all[parentNumber] : null;

            all.Add(
                new DeliveryGroup(occurrence.Label, parent, occurrence.DeferConditionIndex)
                {
                    Id = number,
                    Path = path,
                    IfVariable = occurrence.IfVariable
                });
        }

        var byOccurrence = new DeliveryGroup[occurrences.Count];

        for (var i = 0; i < occurrences.Count; i++)
        {
            byOccurrence[i] = all[occurrences[i].Group!.Value];
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
