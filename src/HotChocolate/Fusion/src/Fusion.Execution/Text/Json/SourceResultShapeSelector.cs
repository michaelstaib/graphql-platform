namespace HotChocolate.Fusion.Text.Json;

/// <summary>
/// Selects the expected shape of each result of a streamed response.
/// </summary>
internal readonly struct SourceResultShapeSelector
{
    private const int MaxIndexDigits = 9;

    private readonly SourceResultShape? _shape;
    private readonly SourceResultShape?[]? _shapes;
    private readonly int _count;

    /// <summary>
    /// Initializes a selector that expects <paramref name="shape"/> of every result.
    /// </summary>
    public SourceResultShapeSelector(SourceResultShape? shape)
    {
        _shape = shape;
    }

    /// <summary>
    /// Initializes a selector that expects of a result the shape at the position that the result
    /// names with <c>requestIndex</c> as its first property. Only the first
    /// <paramref name="count"/> shapes are used.
    /// </summary>
    public SourceResultShapeSelector(SourceResultShape?[] shapes, int count)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, shapes.Length);

        _shapes = shapes;
        _count = count;
    }

    /// <summary>
    /// Gets the expected shape of the result in <paramref name="record"/>, or <c>null</c> when
    /// nothing is expected of it.
    /// </summary>
    public SourceResultShape? GetShape(ReadOnlySpan<byte> record)
    {
        if (_shapes is null)
        {
            return _shape;
        }

        return TryReadRequestIndex(record, out var index) && (uint)index < (uint)_count
            ? _shapes[index]
            : null;
    }

    /// <summary>
    /// Reads the value of <c>requestIndex</c> when it is the first property of the result in
    /// <paramref name="record"/> and a non-negative integer.
    /// </summary>
    internal static bool TryReadRequestIndex(ReadOnlySpan<byte> record, out int index)
    {
        var name = "\"requestIndex\""u8;
        index = 0;

        var position = SkipWhitespace(record, 0);

        if (position == record.Length || record[position] != (byte)'{')
        {
            return false;
        }

        position = SkipWhitespace(record, position + 1);

        if (!record[position..].StartsWith(name))
        {
            return false;
        }

        position = SkipWhitespace(record, position + name.Length);

        if (position == record.Length || record[position] != (byte)':')
        {
            return false;
        }

        position = SkipWhitespace(record, position + 1);
        var start = position;

        while (position < record.Length && (uint)(record[position] - '0') <= 9)
        {
            if (position - start == MaxIndexDigits)
            {
                return false;
            }

            index = (index * 10) + (record[position] - '0');
            position++;
        }

        // The value is an integer without a leading zero that ends within the record.
        if (position == start
            || position == record.Length
            || (record[start] == (byte)'0' && position - start > 1))
        {
            return false;
        }

        return record[position] is (byte)',' or (byte)'}' or (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    private static int SkipWhitespace(ReadOnlySpan<byte> record, int position)
    {
        while (position < record.Length
            && record[position] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            position++;
        }

        return position;
    }
}
