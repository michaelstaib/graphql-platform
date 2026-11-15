using System.Collections.Immutable;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Text.Json;

public sealed partial class CompositeResultDocument
{
    private SinglePlanDeliveryState? _delivery;

    /// <summary>
    /// Gets the delivery state of the request, or <c>null</c> when the request has no delivery groups.
    /// </summary>
    internal SinglePlanDeliveryState? Delivery => _delivery;

    /// <summary>
    /// Makes the document decide per request where each field is delivered. Objects that already exist
    /// are re-stamped, so this has to be called before any data is added.
    /// </summary>
    internal void AttachDelivery(SinglePlanDeliveryState delivery)
    {
        _delivery = delivery;

        var selections = _operation.RootSelectionSet.Selections;
        var root = Cursor.CreateZero();

        for (var index = 0; index < selections.Length; index++)
        {
            var propertyCursor = root.AddRows((index * 2) + 1);
            var flags = _metaDb.GetFlags(propertyCursor);
            var resolution = delivery.Resolve(selections[index]);

            if (resolution.Kind is DeliveryKind.Initial)
            {
                _metaDb.SetFlags(propertyCursor, flags & ~ElementFlags.IsExcluded);
                continue;
            }

            _metaDb.SetFlags(propertyCursor, flags | ElementFlags.IsExcluded);

            if (resolution.Kind is DeliveryKind.Groups)
            {
                _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Held);
            }
        }
    }

    private Cursor AppendDeliveryObjectBlock(
        Cursor parent,
        ObjectTemplate template,
        ReadOnlySpan<Selection> selections)
    {
        var block = _delivery!.GetBlock(selections);
        var start = _metaDb.AppendObjectBlock(template.Rows, parent.Value, block.ExcludedRowOffsets);

        foreach (var offset in block.HeldRowOffsets)
        {
            _metaDb.OrDeliveryMarks(start.AddRows(offset), PropertyMarks.Held);
        }

        return start;
    }

    /// <summary>
    /// Determines whether the merge of a marked step may write <paramref name="field"/> or go below it.
    /// </summary>
    internal bool AllowsMarkedWrite(
        CompositeResultElement field,
        Selection selection,
        ImmutableArray<int> deliveryGroups,
        bool sourceIsContainer)
    {
        if (selection.IsInternal)
        {
            return true;
        }

        var propertyCursor = field.Cursor.AddRows(-1);
        var marks = _metaDb.Get(propertyCursor).DeliveryMarks;
        var token = _metaDb.Get(field.Cursor).TokenType;

        if (token is not ElementTokenType.None || (marks & PropertyMarks.Written) != 0)
        {
            return sourceIsContainer
                && !selection.IsLeaf
                && token is ElementTokenType.Reference or ElementTokenType.StartObject or ElementTokenType.StartArray;
        }

        var resolution = _delivery!.Resolve(selection);

        if (resolution.Kind is DeliveryKind.Absent)
        {
            return true;
        }

        if (resolution.Kind is not DeliveryKind.Groups
            || !(SinglePlanDeliveryState.Intersects(resolution, deliveryGroups)
                || _delivery.IsKeyOrRequirement(selection)))
        {
            return false;
        }

        _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Written);
        return true;
    }

    /// <summary>
    /// Claims the rows at and below <paramref name="anchor"/> that resolve to
    /// <paramref name="deliveryGroupId"/>, are not delivered yet and hold a value or the result of a marked
    /// step, and lists the objects that hold a claimed row with their path below the anchor.
    /// </summary>
    /// <returns><c>true</c> when at least one row was claimed.</returns>
    internal bool ClaimDeliveryRows(
        Cursor anchor,
        int deliveryGroupId,
        Path path,
        List<(Cursor Cursor, Path Path)> roots)
    {
        var before = roots.Count;
        SearchClaims(anchor, _metaDb.Get(anchor), deliveryGroupId, path, roots);

        return roots.Count > before;
    }

    /// <summary>
    /// Writes the rows that <see cref="ClaimDeliveryRows"/> claimed for <paramref name="deliveryGroupId"/>
    /// in the object at <paramref name="root"/>.
    /// </summary>
    internal void WriteDeliveryRows(JsonWriter writer, Cursor root, int deliveryGroupId)
    {
        var row = _metaDb.Get(root);

        if (row.TokenType is ElementTokenType.Reference)
        {
            root = new Cursor(row.Location);
            row = _metaDb.Get(root);
        }

        WriteClaimedObject(writer, root, row, deliveryGroupId, below: false);
    }

    private bool IsContainer(ref Cursor cursor, ref DbRow row)
    {
        if (row.TokenType is ElementTokenType.Reference)
        {
            cursor = new Cursor(row.Location);
            row = _metaDb.Get(cursor);
        }

        return row.TokenType is ElementTokenType.StartObject or ElementTokenType.StartArray
            && (ElementFlags.SourceResult & row.Flags) != ElementFlags.SourceResult
            && (row.Flags & ElementFlags.Invalidated) != ElementFlags.Invalidated;
    }

    private void SearchClaims(
        Cursor cursor,
        DbRow row,
        int groupId,
        Path path,
        List<(Cursor Cursor, Path Path)> roots)
    {
        if (!IsContainer(ref cursor, ref row))
        {
            return;
        }

        if (row.TokenType is ElementTokenType.StartArray)
        {
            var length = row.SizeOrLength;

            for (var i = 0; i < length; i++)
            {
                var elementCursor = cursor.AddRows(i + 1);
                SearchClaims(elementCursor, _metaDb.Get(elementCursor), groupId, path.Append(i), roots);
            }

            return;
        }

        var index = roots.Count;
        var any = false;
        var count = row.SizeOrLength;

        for (var i = 0; i < count; i++)
        {
            var propertyCursor = cursor.AddRows((i * 2) + 1);
            var property = _metaDb.Get(propertyCursor);
            var flags = property.Flags;

            if ((flags & ElementFlags.IsInternal) == ElementFlags.IsInternal)
            {
                continue;
            }

            var marks = property.DeliveryMarks;
            var valueCursor = propertyCursor.AddRows(1);
            var valueRow = _metaDb.Get(valueCursor);
            var selection = _operation.GetSelectionById(property.OperationReferenceId);

            if ((marks & PropertyMarks.Held) != 0)
            {
                if ((marks & PropertyMarks.Delivered) != 0)
                {
                    // Another payload sent the row. The rows below it can still be this group's.
                    SearchClaims(valueCursor, valueRow, groupId, path.Append(selection.ResponseName), roots);
                }
                else if (TryClaim(propertyCursor, marks, valueRow, selection, groupId))
                {
                    any = true;
                    ClaimBelow(valueCursor, valueRow, groupId);
                }

                continue;
            }

            if ((flags & ElementFlags.IsExcluded) != ElementFlags.IsExcluded)
            {
                SearchClaims(valueCursor, valueRow, groupId, path.Append(selection.ResponseName), roots);
            }
        }

        if (any)
        {
            roots.Insert(index, (cursor, path));
        }
    }

    private bool TryClaim(Cursor propertyCursor, int marks, DbRow valueRow, Selection selection, int groupId)
    {
        if (((marks & PropertyMarks.Written) == 0 && valueRow.TokenType is ElementTokenType.None)
            || !SinglePlanDeliveryState.Contains(_delivery!.Resolve(selection), groupId))
        {
            return false;
        }

        _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Claim(0, groupId));
        return true;
    }

    private void ClaimBelow(Cursor cursor, DbRow row, int groupId)
    {
        if (!IsContainer(ref cursor, ref row))
        {
            return;
        }

        if (row.TokenType is ElementTokenType.StartArray)
        {
            var length = row.SizeOrLength;

            for (var i = 0; i < length; i++)
            {
                var elementCursor = cursor.AddRows(i + 1);
                ClaimBelow(elementCursor, _metaDb.Get(elementCursor), groupId);
            }

            return;
        }

        var count = row.SizeOrLength;

        for (var i = 0; i < count; i++)
        {
            var propertyCursor = cursor.AddRows((i * 2) + 1);
            var property = _metaDb.Get(propertyCursor);

            if ((property.Flags & ElementFlags.IsInternal) == ElementFlags.IsInternal)
            {
                continue;
            }

            var marks = property.DeliveryMarks;
            var valueCursor = propertyCursor.AddRows(1);
            var valueRow = _metaDb.Get(valueCursor);

            if ((marks & PropertyMarks.Held) != 0)
            {
                if ((marks & PropertyMarks.Delivered) == 0
                    && TryClaim(
                        propertyCursor,
                        marks,
                        valueRow,
                        _operation.GetSelectionById(property.OperationReferenceId),
                        groupId))
                {
                    ClaimBelow(valueCursor, valueRow, groupId);
                }

                continue;
            }

            if ((property.Flags & ElementFlags.IsExcluded) != ElementFlags.IsExcluded)
            {
                ClaimBelow(valueCursor, valueRow, groupId);
            }
        }
    }

    private static bool IsClaimedBy(int marks, int groupId)
        => (marks & (PropertyMarks.Held | PropertyMarks.Delivered)) == (PropertyMarks.Held | PropertyMarks.Delivered)
            && PropertyMarks.GetClaimant(marks) == groupId;

    // Writes the claimed rows of one object. Below a claimed row every row that is not held is written too.
    private void WriteClaimedObject(JsonWriter writer, Cursor start, DbRow startRow, int groupId, bool below)
    {
        var count = startRow.SizeOrLength;

        writer.WriteStartObject();

        for (var i = 0; i < count; i++)
        {
            var propertyCursor = start.AddRows((i * 2) + 1);
            var property = _metaDb.Get(propertyCursor);
            var flags = property.Flags;

            if ((flags & ElementFlags.IsInternal) == ElementFlags.IsInternal)
            {
                continue;
            }

            var marks = property.DeliveryMarks;
            var held = (marks & PropertyMarks.Held) != 0;
            var excluded = (flags & ElementFlags.IsExcluded) == ElementFlags.IsExcluded;

            if (held ? !IsClaimedBy(marks, groupId) : !below || excluded)
            {
                continue;
            }

            var valueCursor = propertyCursor.AddRows(1);

            writer.WritePropertyName(_operation.GetSelectionById(property.OperationReferenceId).Utf8ResponseName);
            WriteBelow(writer, valueCursor, _metaDb.Get(valueCursor), groupId);
        }

        writer.WriteEndObject();
    }

    private void WriteBelow(JsonWriter writer, Cursor cursor, DbRow row, int groupId)
    {
        if (row.TokenType is ElementTokenType.Reference)
        {
            cursor = new Cursor(row.Location);
            row = _metaDb.Get(cursor);
        }

        if ((row.Flags & ElementFlags.Invalidated) == ElementFlags.Invalidated)
        {
            writer.WriteNullValue();
            return;
        }

        var isSourceResult = (ElementFlags.SourceResult & row.Flags) == ElementFlags.SourceResult;

        switch (row.TokenType)
        {
            case ElementTokenType.StartObject when !isSourceResult:
                WriteClaimedObject(writer, cursor, row, groupId, below: true);
                return;

            case ElementTokenType.StartArray when !isSourceResult:
                var length = row.SizeOrLength;
                writer.WriteStartArray();

                for (var i = 0; i < length; i++)
                {
                    var elementCursor = cursor.AddRows(i + 1);
                    WriteBelow(writer, elementCursor, _metaDb.Get(elementCursor), groupId);
                }

                writer.WriteEndArray();
                return;

            default:
                new RawJsonFormatter(this, writer).WriteValue(cursor, row);
                return;
        }
    }
}
