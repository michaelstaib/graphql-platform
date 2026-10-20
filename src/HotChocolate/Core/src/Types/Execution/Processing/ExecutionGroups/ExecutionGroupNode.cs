using System.Collections.Immutable;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// The position of a selection in a resolved execution group.
/// </summary>
internal sealed class ExecutionGroupNode
{
    internal ExecutionGroupNode(int slotIndex, ImmutableArray<int> descendantSlotIndexes)
    {
        SlotIndex = slotIndex;
        DescendantSlotIndexes = descendantSlotIndexes;
    }

    /// <summary>
    /// Gets the index of the slot of this selection, or <c>-1</c> if the selection only leads to members.
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Gets the indexes of the slots that are only reachable through this selection.
    /// </summary>
    public ImmutableArray<int> DescendantSlotIndexes { get; }
}
