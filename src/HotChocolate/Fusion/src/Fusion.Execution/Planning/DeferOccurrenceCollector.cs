using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Collects leaf field occurrences together with their enclosing delivery
/// group. The collected occurrences are grouped later by effective delivery
/// group set to produce incremental plans.
/// </summary>
internal static class DeferOccurrenceCollector
{
    /// <summary>
    /// The response name of the placeholder occurrence that delivers a composite field
    /// without selecting any of its fields. It is not a valid GraphQL name.
    /// </summary>
    internal const string PlaceholderResponseName = "$placeholder";

    /// <summary>
    /// Collects leaf field occurrences from the given operation, optionally
    /// folding equivalent unlabeled nested <c>@defer</c> fragments into their
    /// parent delivery group.
    /// </summary>
    /// <param name="operation">The operation definition to walk.</param>
    /// <param name="byFragment">
    /// The <see cref="InlineFragmentNode"/> to <see cref="DeliveryGroup"/>
    /// lookup produced by <see cref="DeferPartitioner"/>.
    /// </param>
    /// <param name="inlineUnlabeledNestedDefers">
    /// When <c>true</c> an unlabeled nested <c>@defer</c> whose <c>if</c>
    /// variable matches its parent is folded into the parent's set.
    /// </param>
    public static List<FieldOccurrence> Collect(
        OperationDefinitionNode operation,
        IReadOnlyDictionary<InlineFragmentNode, DeliveryGroup> byFragment,
        bool inlineUnlabeledNestedDefers)
    {
        var occurrences = new List<FieldOccurrence>();
        var compositeFields = new Dictionary<FieldLocation, List<DeliveryGroup?>>();
        CollectOccurrences(
            operation.SelectionSet.Selections,
            parentPath: [],
            enclosingDefer: null,
            parentTypeCondition: null,
            byFragment,
            inlineUnlabeledNestedDefers,
            occurrences,
            compositeFields);
        RemoveRedundantPlaceholders(occurrences, compositeFields);
        return occurrences;
    }

    private static void CollectOccurrences(
        IReadOnlyList<ISelectionNode> selections,
        ImmutableArray<FieldPathSegment> parentPath,
        DeliveryGroup? enclosingDefer,
        NamedTypeNode? parentTypeCondition,
        IReadOnlyDictionary<InlineFragmentNode, DeliveryGroup> byFragment,
        bool inlineUnlabeledNestedDefers,
        List<FieldOccurrence> occurrences,
        Dictionary<FieldLocation, List<DeliveryGroup?>> compositeFields)
    {
        foreach (var selection in selections)
        {
            if (selection is FieldNode fieldNode)
            {
                if (fieldNode.SelectionSet is { } childSelectionSet)
                {
                    // Composite fields define the path for child leaves. Only
                    // leaf fields contribute to delivery group sets. The active
                    // type condition is recorded on the segment so the composite
                    // field can be re-wrapped in an inline fragment when it is
                    // reconstructed under an abstract-typed parent field.
                    var childPath = parentPath.Add(
                        new FieldPathSegment(
                            fieldNode.Name.Value,
                            fieldNode.Alias?.Value,
                            parentTypeCondition?.Name.Value));

                    var location = new FieldLocation(childPath, string.Empty);

                    if (!compositeFields.TryGetValue(location, out var groups))
                    {
                        groups = [];
                        compositeFields[location] = groups;
                    }

                    groups.Add(enclosingDefer);

                    var firstChildOccurrence = occurrences.Count;

                    CollectOccurrences(
                        childSelectionSet.Selections,
                        childPath,
                        enclosingDefer,
                        parentTypeCondition: null,
                        byFragment,
                        inlineUnlabeledNestedDefers,
                        occurrences,
                        compositeFields);

                    // Adds a placeholder occurrence for a composite field that has no fields of its own
                    // in the enclosing group.
                    if (enclosingDefer is not null
                        && !HasOccurrenceInGroup(occurrences, firstChildOccurrence, enclosingDefer))
                    {
                        occurrences.Add(
                            new FieldOccurrence(
                                childPath,
                                PlaceholderResponseName,
                                DeferOperationRewriter.PlaceholderTypeNameField,
                                enclosingDefer,
                                TypeCondition: null));
                    }
                }
                else
                {
                    // Leaf fields contribute at the current path. Sibling
                    // @defer fragments that share a leaf are unified into a
                    // single incremental plan.
                    occurrences.Add(
                        new FieldOccurrence(
                            parentPath,
                            fieldNode.Alias?.Value ?? fieldNode.Name.Value,
                            fieldNode,
                            enclosingDefer,
                            parentTypeCondition));
                }

                continue;
            }

            if (selection is InlineFragmentNode inlineFragment)
            {
                var nestedDefer = enclosingDefer;

                if (byFragment.TryGetValue(inlineFragment, out var canonical))
                {
                    // An unlabeled nested @defer with the same condition as
                    // its parent shares the parent's delivery group when
                    // inlining is enabled.
                    if (inlineUnlabeledNestedDefers
                        && canonical.Label is null
                        && enclosingDefer is not null
                        && (canonical.IfVariable is null
                            || canonical.IfVariable == enclosingDefer.IfVariable))
                    {
                        // Treat as non-defer: keep enclosingDefer.
                    }
                    else
                    {
                        nestedDefer = canonical;
                    }
                }

                CollectOccurrences(
                    inlineFragment.SelectionSet.Selections,
                    parentPath,
                    nestedDefer,
                    inlineFragment.TypeCondition ?? parentTypeCondition,
                    byFragment,
                    inlineUnlabeledNestedDefers,
                    occurrences,
                    compositeFields);
            }
        }
    }

    private static bool HasOccurrenceInGroup(
        List<FieldOccurrence> occurrences,
        int start,
        DeliveryGroup deliveryGroup)
    {
        for (var i = start; i < occurrences.Count; i++)
        {
            if (ReferenceEquals(occurrences[i].EnclosingDeliveryGroup, deliveryGroup))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveRedundantPlaceholders(
        List<FieldOccurrence> occurrences,
        Dictionary<FieldLocation, List<DeliveryGroup?>> compositeFields)
    {
        occurrences.RemoveAll(occurrence =>
            occurrence.ResponseName.Equals(PlaceholderResponseName, StringComparison.Ordinal)
            && IsDeliveredByEnclosingScope(
                compositeFields[new FieldLocation(occurrence.ParentPath, string.Empty)],
                occurrence.EnclosingDeliveryGroup!));
    }

    private static bool IsDeliveredByEnclosingScope(List<DeliveryGroup?> groups, DeliveryGroup deliveryGroup)
    {
        foreach (var group in groups)
        {
            if (group is null)
            {
                return true;
            }

            for (var ancestor = deliveryGroup.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, group))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
