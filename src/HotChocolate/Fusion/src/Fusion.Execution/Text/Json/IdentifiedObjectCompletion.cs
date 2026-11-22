using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HotChocolate.Fusion.Execution.Nodes;

namespace HotChocolate.Fusion.Text.Json;

/// <summary>
/// Completes the properties of a source object that is identified as an object of its shape node
/// into a result object. <see cref="MoveNext"/> assigns every string, number and boolean whose
/// selection takes it unchanged and stops at every other selected property, which the caller
/// completes from <see cref="Selection"/> and <see cref="Value"/>.
/// </summary>
internal ref struct IdentifiedObjectCompletion
{
    private readonly ref byte _sourceRows;
    private readonly ref byte _targetRows;
    private readonly SourceResultDocument _source;
    private readonly CompositeResultDocument _target;
    private readonly SourceResultShape.NodeSelections _selections;
    private readonly uint _sourceCursor;
    private readonly int _endRow;
    private int _nextRow;
    private int _field;
    private int _row;
    private SourceResultDocument.DbRow _value;

    private IdentifiedObjectCompletion(
        ref byte sourceRows,
        ref byte targetRows,
        SourceResultDocument source,
        CompositeResultDocument target,
        SourceResultShape.NodeSelections selections,
        uint sourceCursor,
        int endRow)
    {
        _sourceRows = ref sourceRows;
        _targetRows = ref targetRows;
        _source = source;
        _target = target;
        _selections = selections;
        _sourceCursor = sourceCursor;
        _endRow = endRow;

        // The first row of the object is followed by the name row of its first property.
        _nextRow = 2;
        _field = -1;
        _row = 0;
        _value = default;
    }

    /// <summary>
    /// Gets the selection of the property that <see cref="MoveNext"/> stopped at.
    /// </summary>
    public readonly Selection Selection
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _selections.Selections[_field]!;
    }

    /// <summary>
    /// Gets the value of the property that <see cref="MoveNext"/> stopped at.
    /// </summary>
    public readonly SourceResultElementSnapshot Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_source, SourceResultDocument.Cursor.FromValue(_sourceCursor + (uint)_row), _value);
    }

    /// <summary>
    /// Creates the completion of <paramref name="source"/> into <paramref name="target"/>.
    /// <paramref name="selections"/> must be the selections of the selection set of
    /// <paramref name="target"/> for the shape node that <paramref name="source"/> is identified
    /// as an object of.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the rows of both objects lie in one chunk each. Otherwise <c>false</c>,
    /// and nothing is known about the properties of <paramref name="source"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        scoped in SourceResultElementSnapshot source,
        scoped in CompositeObjectContext target,
        SourceResultShape.NodeSelections selections,
        out IdentifiedObjectCompletion completion)
    {
        Debug.Assert(source._row.TokenType is JsonTokenType.StartObject);
        Debug.Assert(source._parent.GetObjectShapeNode(source._cursor, source._row) >= 0);
        Debug.Assert(target.HasSelectionProperties);
        Debug.Assert(ReferenceEquals(selections.SelectionSet, target.SelectionSet));

        if (selections.ObjectRowCount == target.NumberOfRows)
        {
            var document = source._parent;
            var rowCount = source._row.NumberOfRows;
            ref var sourceRows = ref document.GetRows(source._cursor, rowCount);

            if (!Unsafe.IsNullRef(ref sourceRows))
            {
                ref var targetRows = ref target.GetRows();

                if (!Unsafe.IsNullRef(ref targetRows))
                {
                    completion = new IdentifiedObjectCompletion(
                        ref sourceRows,
                        ref targetRows,
                        document,
                        target.Document,
                        selections,
                        source._cursor.Value,
                        endRow: rowCount - 1);
                    return true;
                }
            }
        }

        completion = default;
        return false;
    }

    /// <summary>
    /// Assigns the values of the next properties that their selections take unchanged, and stops
    /// at the first selected property that needs to be completed by the caller.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the completion stopped at a property;
    /// <c>false</c>, if there are no more properties.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool MoveNext()
    {
        ref var sourceRows = ref _sourceRows;
        ref var targetRows = ref _targetRows;
        var target = _target;
        var valueRowOffsets = _selections.ValueRowOffsets;
        var sourceDocumentId = _source.Id;
        var endRow = _endRow;
        var row = _nextRow;
        var field = _field;

        while (row < endRow)
        {
            var value = Unsafe.ReadUnaligned<SourceResultDocument.DbRow>(
                ref Unsafe.Add(ref sourceRows, row * SourceResultDocument.DbRow.Size));
            var valueRowOffset = valueRowOffsets[++field];
            var tokenType = value.TokenType;

            if (valueRowOffset > 0 && tokenType is >= JsonTokenType.String and <= JsonTokenType.False)
            {
                if (sourceDocumentId < 0)
                {
                    // The property is read again once its document is registered.
                    _nextRow = row;
                    _field = field - 1;
                    return RegisterSourceAndMoveNext();
                }

                target.AssignSourceLeafValue(
                    ref Unsafe.Add(ref targetRows, valueRowOffset),
                    value,
                    sourceDocumentId);

                // A leaf value is followed by the name row of the next property.
                row += 2;
                continue;
            }

            // The next value follows the rows of this value and the name row of the next property.
            var nextRow = row + (value.IsSimpleValue ? 1 : value.NumberOfRows) + 1;

            if (valueRowOffset != SourceResultShape.NodeSelections.NotSelected)
            {
                _row = row;
                _value = value;
                _nextRow = nextRow;
                _field = field;
                return true;
            }

            row = nextRow;
        }

        _nextRow = row;
        _field = field;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool RegisterSourceAndMoveNext()
    {
        _target.GetSourceDocumentId(_source);
        return MoveNext();
    }
}
