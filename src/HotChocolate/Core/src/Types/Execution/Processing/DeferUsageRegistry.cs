using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Holds the defer usages and the interned defer usage sets of one operation.
/// Writes happen only while the operation is compiled, under the compile lock of the operation.
/// </summary>
internal sealed class DeferUsageRegistry
{
    private readonly Dictionary<(string?, DeferUsage?, int), DeferUsage> _usages = [];
    private readonly Dictionary<DeferUsageSet, DeferUsageSet> _setLookup = [];
    private DeferUsageSet[] _sets = [];

    /// <summary>
    /// Gets a snapshot of the interned defer usage sets, ordered by <see cref="DeferUsageSet.Index"/>.
    /// </summary>
    public ImmutableArray<DeferUsageSet> Sets
        => ImmutableCollectionsMarshal.AsImmutableArray(Volatile.Read(ref _sets));

    /// <summary>
    /// Gets the defer usage for the specified values, creating it with the next ordinal if it does not exist.
    /// </summary>
    public DeferUsage GetOrCreateUsage(string? label, DeferUsage? parent, int deferConditionIndex)
    {
        var key = (label, parent, deferConditionIndex);

        if (!_usages.TryGetValue(key, out var usage))
        {
            usage = new DeferUsage(label, parent, deferConditionIndex) { Ordinal = _usages.Count };
            _usages.Add(key, usage);
        }

        return usage;
    }

    /// <summary>
    /// Gets the interned defer usage set of the distinct <paramref name="usages"/>, which may be in any order.
    /// </summary>
    public DeferUsageSet Intern(DeferUsage[] usages)
    {
        var sorted = usages.ToArray();
        Array.Sort(sorted, static (x, y) => x.Ordinal.CompareTo(y.Ordinal));

        var candidate = new DeferUsageSet(sorted, _sets.Length);

        if (_setLookup.TryGetValue(candidate, out var set))
        {
            return set;
        }

        _setLookup.Add(candidate, candidate);

        var sets = new DeferUsageSet[_sets.Length + 1];
        _sets.AsSpan().CopyTo(sets);
        sets[^1] = candidate;
        Volatile.Write(ref _sets, sets);

        return candidate;
    }
}
