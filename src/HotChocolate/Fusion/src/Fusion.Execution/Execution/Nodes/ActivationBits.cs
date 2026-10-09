using System.Buffers;
using System.Runtime.CompilerServices;

namespace HotChocolate.Fusion.Execution.Nodes;

/// <summary>
/// A set of flags indexed from zero. A set of at most 64 flags is held in a single word, a larger
/// set rents its additional words from the given array pool until <see cref="Return"/> is called.
/// </summary>
internal struct ActivationBits
{
    private ulong _word0;
    private ulong[]? _overflow;

    /// <summary>
    /// Initializes a set that holds <paramref name="capacity"/> cleared flags, renting the words
    /// beyond the first 64 flags from <paramref name="pool"/>.
    /// </summary>
    public ActivationBits(int capacity, ArrayPool<ulong> pool)
    {
        if (capacity > 64)
        {
            var wordCount = (capacity - 1) >> 6;
            _overflow = pool.Rent(wordCount);
            _overflow.AsSpan(0, wordCount).Clear();
        }
    }

    /// <summary>
    /// Gets whether the flag at <paramref name="index"/> is set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Get(int index)
        => index < 64
            ? (_word0 & (1ul << index)) != 0
            : (_overflow![(index >> 6) - 1] & (1ul << (index & 63))) != 0;

    /// <summary>
    /// Sets the flag at <paramref name="index"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index)
    {
        if (index < 64)
        {
            _word0 |= 1ul << index;
        }
        else
        {
            _overflow![(index >> 6) - 1] |= 1ul << (index & 63);
        }
    }

    /// <summary>
    /// Returns the rented memory to <paramref name="pool"/>, which must be the pool the set was created
    /// with, and empties the set. Only one copy of the set may call this, once, and no other copy may be
    /// read afterwards.
    /// </summary>
    public void Return(ArrayPool<ulong> pool)
    {
        var overflow = _overflow;
        this = default;

        if (overflow is not null)
        {
            pool.Return(overflow);
        }
    }
}
