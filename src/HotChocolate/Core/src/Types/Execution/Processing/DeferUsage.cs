namespace HotChocolate.Execution.Processing;

/// <summary>
/// Represents a usage of the <c>@defer</c> directive encountered during operation compilation.
/// Forms a parent chain to model nested defer scopes.
/// </summary>
/// <param name="Label">
/// The optional label from <c>@defer(label: "...")</c>, used to identify the deferred
/// payload in the incremental delivery response.
/// </param>
/// <param name="Parent">
/// The parent defer usage when this <c>@defer</c> is nested inside another deferred fragment,
/// or <c>null</c> if this is a top-level defer.
/// </param>
/// <param name="DeferConditionIndex">
/// The index into the <see cref="DeferConditionCollection"/> for the <c>if</c> condition
/// associated with this defer directive. This index maps to a bit position in the
/// runtime defer flags bitmask.
/// </param>
public sealed record DeferUsage(
    string? Label,
    DeferUsage? Parent,
    int DeferConditionIndex)
{
    /// <summary>
    /// Gets the creation order of this defer usage within its operation.
    /// Equal defer usages of an operation share one instance and therefore one ordinal.
    /// </summary>
    public int Ordinal { get; internal init; }

    /// <inheritdoc />
    public bool Equals(DeferUsage? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && DeferConditionIndex == other.DeferConditionIndex
            && string.Equals(Label, other.Label, StringComparison.Ordinal)
            && Equals(Parent, other.Parent);
    }

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Label, Parent, DeferConditionIndex);

    /// <summary>
    /// Gets the nearest defer usage in the parent chain of <paramref name="usage"/>, starting with
    /// <paramref name="usage"/> itself, whose condition is enabled, or <c>null</c> if none is enabled.
    /// </summary>
    internal static DeferUsage? FindActive(DeferUsage? usage, ulong deferFlags, ReadOnlySpan<ulong> wideDeferFlags)
    {
        while (usage is not null && !IsConditionBitSet(deferFlags, wideDeferFlags, usage.DeferConditionIndex))
        {
            usage = usage.Parent;
        }

        return usage;
    }

    private static bool IsConditionBitSet(ulong flags, ReadOnlySpan<ulong> wideFlags, int index)
    {
        var word = index >> 6;
        var bit = 1ul << (index & 63);

        if (word == 0)
        {
            return (flags & bit) != 0;
        }

        word--;
        return (uint)word < (uint)wideFlags.Length && (wideFlags[word] & bit) != 0;
    }
}
