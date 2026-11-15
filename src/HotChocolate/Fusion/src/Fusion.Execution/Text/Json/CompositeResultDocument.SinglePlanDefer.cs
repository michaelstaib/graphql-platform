using System.Collections.Immutable;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Text.Json;

public sealed partial class CompositeResultDocument
{
    private SinglePlanDeliveryState? _delivery;

    /// <summary>
    /// Makes the document decide per request where each field is delivered. Objects that already exist
    /// are re-stamped, so this has to be called before any data is added.
    /// </summary>
    internal void AttachDelivery(SinglePlanDeliveryState delivery)
    {
        _delivery = delivery;

        var template = _operation.RootSelectionSet.ObjectTemplate;
        var selections = _operation.RootSelectionSet.Selections;
        var root = Cursor.CreateZero();

        foreach (var index in template.ConditionalSelections)
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
        var conditionalSelections = template.ConditionalSelections;
        var excluded = conditionalSelections.Length <= 128
            ? stackalloc int[conditionalSelections.Length]
            : new int[conditionalSelections.Length];
        var held = conditionalSelections.Length <= 128
            ? stackalloc int[conditionalSelections.Length]
            : new int[conditionalSelections.Length];
        var excludedCount = 0;
        var heldCount = 0;

        foreach (var index in conditionalSelections)
        {
            var resolution = _delivery!.Resolve(selections[index]);

            if (resolution.Kind is DeliveryKind.Initial)
            {
                continue;
            }

            excluded[excludedCount++] = (index * 2) + 1;

            if (resolution.Kind is DeliveryKind.Groups)
            {
                held[heldCount++] = (index * 2) + 1;
            }
        }

        var start = _metaDb.AppendObjectBlock(template.Rows, parent.Value, excluded[..excludedCount]);

        for (var i = 0; i < heldCount; i++)
        {
            _metaDb.OrDeliveryMarks(start.AddRows(held[i]), PropertyMarks.Held);
        }

        return start;
    }

    /// <summary>
    /// Determines whether the merge of a marked step may write <paramref name="field"/>.
    /// </summary>
    internal bool AllowsMarkedWrite(
        CompositeResultElement field,
        Selection selection,
        ImmutableArray<int> deliveryGroups,
        bool isTargetField)
    {
        if (selection.IsInternal)
        {
            return true;
        }

        var resolution = _delivery!.Resolve(selection);

        if (resolution.Kind is not DeliveryKind.Groups)
        {
            return !isTargetField;
        }

        if (!SinglePlanDeliveryState.Intersects(resolution, deliveryGroups))
        {
            return false;
        }

        if (!isTargetField)
        {
            return true;
        }

        var valueCursor = field.Cursor;
        var marks = _metaDb.Get(valueCursor.AddRows(-1)).DeliveryMarks;

        return (marks & PropertyMarks.Delivered) == 0
            && _metaDb.Get(valueCursor).TokenType is ElementTokenType.None;
    }

    /// <summary>
    /// Claims the rows below <paramref name="anchor"/> that resolve to <paramref name="deliveryGroupId"/>
    /// and are not delivered yet.
    /// </summary>
    /// <returns><c>true</c> when at least one row was claimed.</returns>
    internal bool ClaimDeliveryRows(Cursor anchor, int deliveryGroupId)
    {
        var row = _metaDb.Get(anchor);
        return DeliveryValue(anchor, row, deliveryGroupId, written: false, claim: true, writer: null);
    }

    /// <summary>
    /// Writes the rows below <paramref name="anchor"/> that <see cref="ClaimDeliveryRows"/> claimed for
    /// <paramref name="deliveryGroupId"/>.
    /// </summary>
    internal void WriteDeliveryRows(JsonWriter writer, Cursor anchor, int deliveryGroupId)
    {
        var row = _metaDb.Get(anchor);

        if (!DeliveryValue(anchor, row, deliveryGroupId, written: false, claim: false, writer))
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    private bool DeliveryValue(
        Cursor cursor,
        DbRow row,
        int groupId,
        bool written,
        bool claim,
        JsonWriter? writer)
    {
        if (row.TokenType is ElementTokenType.Reference)
        {
            cursor = new Cursor(row.Location);
            row = _metaDb.Get(cursor);
        }

        var isSourceResult = (ElementFlags.SourceResult & row.Flags) == ElementFlags.SourceResult;

        switch (row.TokenType)
        {
            case ElementTokenType.StartObject when !isSourceResult:
                if ((row.Flags & ElementFlags.Invalidated) == ElementFlags.Invalidated)
                {
                    break;
                }

                return DeliveryObject(cursor, row, groupId, written, claim, writer);

            case ElementTokenType.StartArray when !isSourceResult:
                return DeliveryArray(cursor, row, groupId, written, claim, writer);
        }

        if (!written)
        {
            return false;
        }

        if (writer is not null)
        {
            if ((row.Flags & ElementFlags.Invalidated) == ElementFlags.Invalidated)
            {
                writer.WriteNullValue();
            }
            else
            {
                new RawJsonFormatter(this, writer).WriteValue(cursor, row);
            }
        }

        return true;
    }

    private bool DeliveryObject(
        Cursor start,
        DbRow startRow,
        int groupId,
        bool written,
        bool claim,
        JsonWriter? writer)
    {
        var any = false;
        var count = startRow.SizeOrLength;

        writer?.WriteStartObject();

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
            var selection = _operation.GetSelectionById(property.OperationReferenceId);
            var qualifies = IsClaimedBy(marks, selection, groupId, claim);

            if (!qualifies && (flags & ElementFlags.IsExcluded) == ElementFlags.IsExcluded)
            {
                continue;
            }

            var valueCursor = propertyCursor.AddRows(1);
            var valueRow = _metaDb.Get(valueCursor);

            if (qualifies || written)
            {
                if (qualifies && claim)
                {
                    _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Claim(0, groupId));
                }

                any = true;

                writer?.WritePropertyName(selection.Utf8ResponseName);

                DeliveryValue(valueCursor, valueRow, groupId, written: true, claim, writer);
                continue;
            }

            if (writer is null)
            {
                any |= DeliveryValue(valueCursor, valueRow, groupId, written: false, claim, writer: null);
                continue;
            }

            if (!DeliveryValue(valueCursor, valueRow, groupId, written: false, claim: false, writer: null))
            {
                continue;
            }

            any = true;
            writer.WritePropertyName(selection.Utf8ResponseName);
            DeliveryValue(valueCursor, valueRow, groupId, written: false, claim: false, writer);
        }

        writer?.WriteEndObject();
        return any;
    }

    private bool DeliveryArray(
        Cursor start,
        DbRow startRow,
        int groupId,
        bool written,
        bool claim,
        JsonWriter? writer)
    {
        var any = false;
        var length = startRow.SizeOrLength;

        writer?.WriteStartArray();

        for (var i = 0; i < length; i++)
        {
            var elementCursor = start.AddRows(i + 1);
            var elementRow = _metaDb.Get(elementCursor);

            if (writer is null)
            {
                any |= DeliveryValue(elementCursor, elementRow, groupId, written, claim, writer: null);
                continue;
            }

            var resolvedRow = elementRow;

            if (resolvedRow.TokenType is ElementTokenType.Reference)
            {
                resolvedRow = _metaDb.Get(new Cursor(resolvedRow.Location));
            }

            var isContainer = resolvedRow.TokenType is ElementTokenType.StartObject or ElementTokenType.StartArray
                && (ElementFlags.SourceResult & resolvedRow.Flags) != ElementFlags.SourceResult;

            if (!written && !isContainer)
            {
                writer.WriteNullValue();
                continue;
            }

            any |= DeliveryValue(elementCursor, elementRow, groupId, written, claim: false, writer);
        }

        writer?.WriteEndArray();
        return any;
    }

    private bool IsClaimedBy(int marks, Selection selection, int groupId, bool claim)
    {
        if ((marks & PropertyMarks.Held) == 0)
        {
            return false;
        }

        if (claim)
        {
            return (marks & PropertyMarks.Delivered) == 0
                && SinglePlanDeliveryState.Contains(_delivery!.Resolve(selection), groupId);
        }

        return (marks & PropertyMarks.Delivered) != 0
            && PropertyMarks.GetClaimant(marks) == groupId;
    }
}
