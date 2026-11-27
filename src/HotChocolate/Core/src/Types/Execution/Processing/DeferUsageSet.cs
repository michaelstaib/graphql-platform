namespace HotChocolate.Execution.Processing;

/// <summary>
/// Represents the distinct defer usages that contain a selection, without the usages whose
/// parent chain includes another usage of the set.
/// </summary>
internal sealed class DeferUsageSet : IEquatable<DeferUsageSet>
{
    private readonly int _hashCode;

    /// <summary>
    /// Initializes a new instance of <see cref="DeferUsageSet"/>.
    /// </summary>
    /// <param name="usages">The distinct defer usages of the set, sorted by <see cref="DeferUsage.Ordinal"/>.</param>
    /// <param name="index">
    /// The index of this set in the defer usage sets of its operation, or -1 for a set that only one request uses.
    /// </param>
    public DeferUsageSet(DeferUsage[] usages, int index)
    {
        ArgumentNullException.ThrowIfNull(usages);

        Usages = usages;
        Index = index;

        var hashCode = new HashCode();

        foreach (var usage in usages)
        {
            hashCode.Add(usage);
        }

        _hashCode = hashCode.ToHashCode();
    }

    /// <summary>
    /// Gets the defer usages of this set, sorted by <see cref="DeferUsage.Ordinal"/>.
    /// </summary>
    public DeferUsage[] Usages { get; }

    /// <summary>
    /// Gets the index of this set in the defer usage sets of its operation, or -1 for a set that only one request uses.
    /// </summary>
    public int Index { get; }

    /// <inheritdoc />
    public bool Equals(DeferUsageSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && other._hashCode == _hashCode
            && Usages.AsSpan().SequenceEqual(other.Usages);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DeferUsageSet other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => _hashCode;
}
