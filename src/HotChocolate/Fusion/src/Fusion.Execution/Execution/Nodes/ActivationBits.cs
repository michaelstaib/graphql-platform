using System.Buffers;
using System.Runtime.CompilerServices;

namespace HotChocolate.Fusion.Execution.Nodes;

/// <summary>
/// A set of flags indexed from zero. A set of at most 64 flags is held in a single word, a larger
/// set rents its additional words from the shared array pool until <see cref="Return"/> is called.
/// </summary>
internal struct ActivationBits
{
    private ulong _word0;
    private ulong[]? _overflow;

    /// <summary>
    /// Initializes a set that holds <paramref name="capacity"/> cleared flags.
    /// </summary>
    public ActivationBits(int capacity)
    {
        if (capacity > 64)
        {
            var wordCount = (capacity - 1) >> 6;
            _overflow = ArrayPool<ulong>.Shared.Rent(wordCount);
            _overflow.AsSpan(0, wordCount).Clear();
        }
    }

    /// <summary>
    /// Gets the rented words of a set larger than 64 flags, or <c>null</c> for a smaller set or
    /// after <see cref="Return"/>.
    /// </summary>
    internal readonly ulong[]? RentedWords => _overflow;

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
    /// Returns the rented memory and empties the set. Only one copy of the set may call this, once, and no
    /// other copy may be read afterwards.
    /// </summary>
    public void Return()
    {
        var overflow = _overflow;
        this = default;

        if (overflow is not null)
        {
            ArrayPool<ulong>.Shared.Return(overflow);
        }
    }
}
