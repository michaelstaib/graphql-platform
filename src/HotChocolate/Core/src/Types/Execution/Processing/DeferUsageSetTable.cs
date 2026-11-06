namespace HotChocolate.Execution.Processing;

/// <summary>
/// Resolves the static defer usage set of a selection to its active set for the defer conditions of one request.
/// Each set is resolved once per request, and equal active sets of a request are one instance.
/// </summary>
internal sealed class DeferUsageSetTable
{
    private static readonly DeferUsageSet s_unresolved = new([], -1);

#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly Operation _operation;
    private readonly ulong _deferFlags;
    private readonly ulong[]? _wideDeferFlags;
    private DeferUsageSet?[] _slots;
    private Dictionary<DeferUsageSet, DeferUsageSet>? _interned;

    /// <summary>
    /// Initializes a new instance of <see cref="DeferUsageSetTable"/>.
    /// </summary>
    /// <param name="operation">The operation whose defer usage sets are resolved.</param>
    /// <param name="deferFlags">The defer condition flags of the request.</param>
    public DeferUsageSetTable(Operation operation, ConditionFlags deferFlags)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _operation = operation;
        _deferFlags = deferFlags.Word0;
        _wideDeferFlags = deferFlags.Overflow;
        _slots = CreateSlots(operation.DeferUsageSets.Length);
    }

    /// <summary>
    /// Gets the active defer usage set of <paramref name="selection"/>, or <c>null</c> if the selection
    /// is not deferred in this request.
    /// </summary>
    public DeferUsageSet? GetActive(Selection selection)
    {
        if (selection.DeferUsageSet is not { } set)
        {
            return null;
        }

        var slots = Volatile.Read(ref _slots);

        if ((uint)set.Index < (uint)slots.Length)
        {
            var cached = Volatile.Read(ref slots[set.Index]);

            if (!ReferenceEquals(cached, s_unresolved))
            {
                return cached;
            }
        }

        return ResolveSlow(set);
    }

    private DeferUsageSet? ResolveSlow(DeferUsageSet set)
    {
        lock (_sync)
        {
            var slots = _slots;

            if ((uint)set.Index >= (uint)slots.Length)
            {
                var grown = CreateSlots(Math.Max(_operation.DeferUsageSets.Length, set.Index + 1));
                slots.AsSpan().CopyTo(grown);
                Volatile.Write(ref _slots, grown);
                slots = grown;
            }

            var cached = slots[set.Index];

            if (!ReferenceEquals(cached, s_unresolved))
            {
                return cached;
            }

            var active = Resolve(set);

            if (active is not null)
            {
                _interned ??= [];

                if (_interned.TryGetValue(active, out var interned))
                {
                    active = interned;
                }
                else
                {
                    _interned.Add(active, active);
                }
            }

            Volatile.Write(ref slots[set.Index], active);
            return active;
        }
    }

    private DeferUsageSet? Resolve(DeferUsageSet set)
    {
        var usages = set.Usages;
        var allActive = true;

        foreach (var usage in usages)
        {
            var active = DeferUsage.FindActive(usage, _deferFlags, _wideDeferFlags);

            if (active is null)
            {
                return null;
            }

            if (!ReferenceEquals(active, usage))
            {
                allActive = false;
            }
        }

        if (allActive)
        {
            return set;
        }

        // an inactive usage folds into its nearest active ancestor; the folded usages are deduplicated,
        // and a usage whose parent chain contains another usage of the set is removed.
        var resolved = new List<DeferUsage>(usages.Length);

        foreach (var usage in usages)
        {
            var active = DeferUsage.FindActive(usage, _deferFlags, _wideDeferFlags)!;

            if (!resolved.Contains(active))
            {
                resolved.Add(active);
            }
        }

        for (var i = resolved.Count - 1; i >= 0; i--)
        {
            for (var ancestor = resolved[i].Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (resolved.Contains(ancestor))
                {
                    resolved.RemoveAt(i);
                    break;
                }
            }
        }

        resolved.Sort(static (x, y) => x.Ordinal.CompareTo(y.Ordinal));
        return new DeferUsageSet(resolved.ToArray(), -1);
    }

    private static DeferUsageSet?[] CreateSlots(int length)
    {
        var slots = new DeferUsageSet?[length];
        slots.AsSpan().Fill(s_unresolved);
        return slots;
    }
}
