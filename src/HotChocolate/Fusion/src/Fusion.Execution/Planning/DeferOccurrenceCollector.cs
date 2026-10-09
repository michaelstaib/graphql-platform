using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Collects leaf field occurrences together with their enclosing delivery
/// group, and the conditional <c>@defer</c> fragments nested inside another
/// delivery group. The collected occurrences are grouped later by effective
/// delivery group set to produce incremental plans.
/// </summary>
internal static class DeferOccurrenceCollector
{
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
    public static DeferCollectionResult Collect(
        OperationDefinitionNode operation,
        IReadOnlyDictionary<InlineFragmentNode, DeliveryGroup> byFragment,
        bool inlineUnlabeledNestedDefers)
    {
        var occurrences = ImmutableArray.CreateBuilder<FieldOccurrence>();
        var conditionalFragments = ImmutableArray.CreateBuilder<ConditionalFragmentOccurrence>();
        CollectOccurrences(
            operation.SelectionSet.Selections,
            parentPath: [],
            enclosingDefer: null,
            parentTypeCondition: null,
            byFragment,
            inlineUnlabeledNestedDefers,
            occurrences,
            conditionalFragments);
        return new DeferCollectionResult(occurrences.ToImmutable(), conditionalFragments.ToImmutable());
    }

    private static void CollectOccurrences(
        IReadOnlyList<ISelectionNode> selections,
        ImmutableArray<FieldPathSegment> parentPath,
        DeliveryGroup? enclosingDefer,
        NamedTypeNode? parentTypeCondition,
        IReadOnlyDictionary<InlineFragmentNode, DeliveryGroup> byFragment,
        bool inlineUnlabeledNestedDefers,
        ImmutableArray<FieldOccurrence>.Builder occurrences,
        ImmutableArray<ConditionalFragmentOccurrence>.Builder conditionalFragments)
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

                    CollectOccurrences(
                        childSelectionSet.Selections,
                        childPath,
                        enclosingDefer,
                        parentTypeCondition: null,
                        byFragment,
                        inlineUnlabeledNestedDefers,
                        occurrences,
                        conditionalFragments);
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
                        && canonical.IfVariable == enclosingDefer.IfVariable)
                    {
                        // Treat as non-defer: keep enclosingDefer.
                    }
                    else
                    {
                        // Top-level conditional defers are covered by the main operation.
                        if (canonical.IfVariable is not null && enclosingDefer is not null)
                        {
                            conditionalFragments.Add(
                                new ConditionalFragmentOccurrence(
                                    inlineFragment,
                                    enclosingDefer,
                                    parentPath,
                                    parentTypeCondition));
                        }

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
                    conditionalFragments);
            }
        }
    }
}
