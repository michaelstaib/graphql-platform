using System.Runtime.CompilerServices;

namespace HotChocolate.Fusion.Text.Json;

/// <summary>
/// Identifies the chunk-size bucket a document allocates its memory slabs at.
/// The ordinal maps to a byte size via <c>1 &lt;&lt; (10 + ordinal)</c>, so the
/// bucket fits the three size bits of a <c>Cursor</c>.
/// </summary>
internal enum ChunkSize
{
    /// <summary>1 KB chunks.</summary>
    Size1K,

    /// <summary>2 KB chunks.</summary>
    Size2K,

    /// <summary>4 KB chunks.</summary>
    Size4K,

    /// <summary>8 KB chunks.</summary>
    Size8K,

    /// <summary>16 KB chunks.</summary>
    Size16K,

    /// <summary>32 KB chunks.</summary>
    Size32K,

    /// <summary>64 KB chunks.</summary>
    Size64K,

    /// <summary>128 KB chunks.</summary>
    Size128K
}

/// <summary>
/// The growth schedule that maps a chunk index to its <see cref="ChunkSize"/>. The metadata stores
/// and the data store of the result documents derive the size of a chunk from its index through
/// this schedule.
/// </summary>
internal static class ChunkSchedule
{
    /// <summary>
    /// The index of the first chunk of the constant tail. This chunk and every later chunk has
    /// the size <see cref="ChunkSize.Size128K"/>.
    /// </summary>
    public const int RampLength = 18;

    // Chunks 0..4 double from 1 KB to 16 KB, then the size is held for a while before it doubles
    // again: seven more 16 KB chunks, four 32 KB chunks and two 64 KB chunks. A store that holds
    // n bytes has rented at most n plus one chunk of its current size, which stays below an
    // eighth of n once the store has left the first 16 KB chunks.
    private static ReadOnlySpan<byte> Ordinals =>
    [
        0, 1, 2, 3, 4,
        4, 4, 4, 4, 4, 4, 4,
        5, 5, 5, 5,
        6, 6,
        7
    ];

    /// <summary>
    /// Gets the chunk size of the chunk at the given index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChunkSize SizeFor(int chunkIndex)
        => (ChunkSize)Ordinals[Math.Min(chunkIndex, RampLength)];
}
