using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Result of <see cref="DeferPartitioner.Partition"/>. Carries the complete
/// <see cref="DeliveryGroup"/> tree together with the <see cref="InlineFragmentNode"/>-keyed lookup
/// that resolves any <c>... @defer</c> in the same AST to its <see cref="DeliveryGroup"/>.
/// </summary>
internal sealed class DeferPartitioningResult(
    ImmutableArray<DeliveryGroup> allDeliveryGroups,
    IReadOnlyDictionary<InlineFragmentNode, DeliveryGroup> byFragment,
    string? numberingError)
{
    /// <summary>
    /// Every <see cref="DeliveryGroup"/> of the operation, in the order of their group numbers, or in
    /// declaration order when the operation carries none or they disagree. The array index equals
    /// <see cref="DeliveryGroup.Id"/>.
    /// </summary>
    public ImmutableArray<DeliveryGroup> AllDeliveryGroups { get; } = allDeliveryGroups;

    /// <summary>
    /// Maps each <c>... @defer</c> inline fragment to its canonical
    /// <see cref="DeliveryGroup"/>. Lookup is by reference identity on the AST
    /// node.
    /// </summary>
    public IReadOnlyDictionary<InlineFragmentNode, DeliveryGroup> ByFragment { get; } = byFragment;

    /// <summary>
    /// The reason why the group numbers of the operation disagree, or <c>null</c> when they agree or the
    /// operation has none. With a reason the groups are numbered by position.
    /// </summary>
    public string? NumberingError { get; } = numberingError;
}
