using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Holds the defer usages and the interned defer usage sets of one operation.
/// Writes happen only while the operation is compiled, under the compile lock of the operation.
/// </summary>
internal sealed class DeferUsageRegistry
{
    private readonly Dictionary<(string?, DeferUsage?, int, int), DeferUsage> _usages = [];
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
    public DeferUsage GetOrCreateUsage(string? label, DeferUsage? parent, int deferConditionIndex, int depth)
    {
        var key = (label, parent, deferConditionIndex, depth);

        if (!_usages.TryGetValue(key, out var usage))
        {
            usage = new DeferUsage(label, parent, deferConditionIndex, depth) { Ordinal = _usages.Count };
            _usages.Add(key, usage);
        }

        return usage;
    }

    /// <summary>
    /// Gets the interned defer usage set holding the distinct usages of <paramref name="usages"/>.
    /// </summary>
    public DeferUsageSet Intern(List<DeferUsage> usages)
    {
        usages.Sort(static (x, y) => x.Ordinal.CompareTo(y.Ordinal));

        for (var i = usages.Count - 1; i > 0; i--)
        {
            if (ReferenceEquals(usages[i], usages[i - 1]))
            {
                usages.RemoveAt(i);
            }
        }

        var candidate = new DeferUsageSet(usages.ToArray(), _sets.Length);

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
