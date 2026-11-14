using System.Diagnostics.CodeAnalysis;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Maps the selection set of every <c>@defer</c> fragment of a planned operation to its delivery group.
/// </summary>
internal sealed class DeferredFragmentIndex
{
    private readonly Dictionary<uint, int> _deliveryGroupBySelectionSetId;

    private DeferredFragmentIndex(Dictionary<uint, int> deliveryGroupBySelectionSetId)
    {
        _deliveryGroupBySelectionSetId = deliveryGroupBySelectionSetId;
    }

    /// <summary>
    /// Creates the index for the operation that <paramref name="index"/> and <paramref name="partitioning"/> describe.
    /// </summary>
    /// <returns>
    /// <c>false</c> when two <c>@defer</c> fragments share one selection set.
    /// </returns>
    public static bool TryCreate(
        ISelectionSetIndex index,
        DeferPartitioningResult partitioning,
        [NotNullWhen(true)] out DeferredFragmentIndex? deferredFragments)
    {
        var map = new Dictionary<uint, int>();

        foreach (var (fragment, deliveryGroup) in partitioning.ByFragment)
        {
            if (!map.TryAdd(index.GetId(fragment.SelectionSet), deliveryGroup.Id))
            {
                deferredFragments = null;
                return false;
            }
        }

        deferredFragments = new DeferredFragmentIndex(map);
        return true;
    }

    /// <summary>
    /// Gets the delivery group of the <c>@defer</c> fragment whose selection set, or whose original selection set,
    /// has the identifier <paramref name="selectionSetId"/>.
    /// </summary>
    public bool TryGetDeliveryGroup(ISelectionSetIndex index, uint selectionSetId, out int deliveryGroupId)
    {
        while (true)
        {
            if (_deliveryGroupBySelectionSetId.TryGetValue(selectionSetId, out deliveryGroupId))
            {
                return true;
            }

            if (!index.TryGetOriginalIdFromCloned(selectionSetId, out selectionSetId))
            {
                return false;
            }
        }
    }
}
