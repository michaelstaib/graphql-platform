using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Text.Json;
using HotChocolate.Types;

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
        _delivery!.AssertOnLoop();

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
    /// Claims the rows at and below the anchor object of <paramref name="instance"/> that resolve to its
    /// group, hold a result and are not delivered yet, and lists the objects that hold a claimed row. Each
    /// row goes to the open instance with the longest path among the groups that hold the row.
    /// </summary>
    /// <returns><c>true</c> when at least one row was claimed.</returns>
    internal bool ClaimDeliveryRows(int instance, List<ClaimedObject> roots)
    {
        var delivery = _delivery!;
        delivery.AssertOnLoop();

        var group = delivery.GetInstanceGroup(instance);
        var anchor = delivery.GetInstanceAnchor(instance);
        var depth = delivery.AnchorDepths[group];
        var scratch = delivery.GetClaimScratch();
        var before = roots.Count;

        scratch.Begin(instance, group, depth, roots);
        ClaimRows(anchor, _metaDb.Get(anchor), depth, enclosing: -1, scratch);

        return roots.Count > before;
    }

    /// <summary>
    /// Writes the rows that <see cref="ClaimDeliveryRows"/> claimed for <paramref name="deliveryGroupId"/>
    /// in the object at <paramref name="root"/>.
    /// </summary>
    internal void WriteDeliveryRows(JsonWriter writer, Cursor root, int deliveryGroupId)
    {
        _delivery!.AssertLoopNotRunningElsewhere();

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

    // The enclosing group is the group whose payload entry already writes the position, or -1.
    private void ClaimRows(Cursor cursor, DbRow row, int depth, int enclosing, DeliveryClaimScratch scratch)
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

                scratch.PushIndex(i);
                ClaimRows(elementCursor, _metaDb.Get(elementCursor), depth, enclosing, scratch);
                scratch.Pop();
            }

            return;
        }

        scratch.EnterObject(depth, cursor);

        var frame = scratch.Carriers.Count;
        var rootIndex = scratch.Roots.Count;
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
                    // Another payload sent the row. The rows below it can still be rows of this group.
                    scratch.PushName(selection.ResponseName);
                    ClaimRows(valueCursor, valueRow, depth + 1, enclosing: -1, scratch);
                    scratch.Pop();
                }
                else
                {
                    var resolution = _delivery!.Resolve(selection);

                    if (HoldsResult(marks, valueRow, resolution, scratch.Group))
                    {
                        var (carrierGroup, carrierInstance) = ChooseCarrier(scratch, resolution, depth);

                        _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Claim(0, carrierGroup));

                        if (carrierGroup != scratch.Group)
                        {
                            _delivery.RecordCrossClaim(propertyCursor);
                        }

                        if (carrierGroup != enclosing && !ContainsCarrier(scratch.Carriers, frame, carrierGroup))
                        {
                            scratch.Carriers.Add((carrierGroup, carrierInstance));
                        }

                        scratch.PushName(selection.ResponseName);
                        ClaimRows(valueCursor, valueRow, depth + 1, carrierGroup, scratch);
                        scratch.Pop();
                    }
                }

                continue;
            }

            if ((flags & ElementFlags.IsExcluded) != ElementFlags.IsExcluded)
            {
                scratch.PushName(selection.ResponseName);
                ClaimRows(valueCursor, valueRow, depth + 1, enclosing, scratch);
                scratch.Pop();
            }
        }

        var added = 0;

        for (var i = frame; i < scratch.Carriers.Count; i++)
        {
            var (group, instance) = scratch.Carriers[i];

            // The entry that another walk listed for the object and the group writes these rows too.
            if (_delivery!.ListsObject(cursor, group, add: group != scratch.Group))
            {
                continue;
            }

            var path = scratch.CreatePathFrom(_delivery.AnchorDepths[group]);

            scratch.Roots.Insert(rootIndex + added, new ClaimedObject(cursor, group, instance, path));
            added++;
        }

        scratch.Carriers.RemoveRange(frame, scratch.Carriers.Count - frame);
    }

    private static bool ContainsCarrier(List<(int Group, int Instance)> carriers, int start, int group)
    {
        for (var i = start; i < carriers.Count; i++)
        {
            if (carriers[i].Group == group)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HoldsResult(int marks, DbRow valueRow, DeliveryResolution resolution, int groupId)
        => ((marks & PropertyMarks.Written) != 0 || valueRow.TokenType is not ElementTokenType.None)
            && SinglePlanDeliveryState.Contains(resolution, groupId);

    // The claiming instance sends the row unless an open instance below it holds the row too.
    private (int Group, int Instance) ChooseCarrier(
        DeliveryClaimScratch scratch,
        DeliveryResolution resolution,
        int depth)
    {
        var delivery = _delivery!;
        var group = scratch.Group;
        var instance = scratch.Instance;
        var longest = scratch.BaseDepth;

        foreach (var candidate in resolution.Groups)
        {
            var candidateDepth = delivery.AnchorDepths[candidate.Id];

            if (candidateDepth <= longest
                || candidateDepth > depth
                || !delivery.TryFindOpenInstance(candidate.Id, scratch.GetObject(candidateDepth), out var found))
            {
                continue;
            }

            group = candidate.Id;
            instance = found;
            longest = candidateDepth;
        }

        return (group, instance);
    }

    /// <summary>
    /// Finds the objects of the client data that the group applies to below the scope object. An object is
    /// honored when its selection set holds a selection that resolves to the group, and objects that are not
    /// honored are only listed when <paramref name="keepUnhonored"/> is set.
    /// </summary>
    internal void FindDeliveryAnchors(
        DeliveryGroup group,
        int firstSegment,
        Cursor scope,
        Path scopePath,
        bool keepUnhonored,
        List<DeliveryAnchor> found)
    {
        _delivery!.AssertLoopNotRunningElsewhere();

        var selectionPath = group.Path ?? SelectionPath.Root;

        FindAnchors(scope, scopePath, -1, group.Id, selectionPath, firstSegment, keepUnhonored, found);
    }

    // The path of the position is the parent path followed by the index, when the index is not negative.
    private void FindAnchors(
        Cursor cursor,
        Path parentPath,
        int index,
        int groupId,
        SelectionPath selectionPath,
        int segmentIndex,
        bool keepUnhonored,
        List<DeliveryAnchor> found)
    {
        var row = _metaDb.Get(cursor);

        if (!IsContainer(ref cursor, ref row))
        {
            return;
        }

        if (row.TokenType is ElementTokenType.StartArray)
        {
            var arrayPath = index < 0 ? parentPath : parentPath.Append(index);
            var length = row.SizeOrLength;

            for (var i = 0; i < length; i++)
            {
                FindAnchors(
                    cursor.AddRows(i + 1),
                    arrayPath,
                    i,
                    groupId,
                    selectionPath,
                    segmentIndex,
                    keepUnhonored,
                    found);
            }

            return;
        }

        if (row.OperationReferenceType is not OperationReferenceType.SelectionSet)
        {
            return;
        }

        var selectionSet = _operation.GetSelectionSetById(row.OperationReferenceId);

        while (segmentIndex < selectionPath.Length)
        {
            var segment = selectionPath[segmentIndex];

            if (segment.Kind is SelectionPathSegmentKind.Field)
            {
                if (!TryGetNamedPropertyValue(cursor, segment.Name, out var value))
                {
                    return;
                }

                var propertyCursor = value.Cursor.AddRows(-1);
                var property = _metaDb.Get(propertyCursor);

                if (!IsDelivered(property))
                {
                    return;
                }

                var objectPath = index < 0 ? parentPath : parentPath.Append(index);

                FindAnchors(
                    value.Cursor,
                    objectPath.Append(segment.Name),
                    -1,
                    groupId,
                    selectionPath,
                    segmentIndex + 1,
                    keepUnhonored,
                    found);
                return;
            }

            if (segment.Kind is SelectionPathSegmentKind.InlineFragment
                && !_operation.Schema.Types.GetType<IOutputTypeDefinition>(segment.Name)
                    .IsAssignableFrom(selectionSet.Type))
            {
                return;
            }

            segmentIndex++;
        }

        if (_delivery!.IsHonoredAt(groupId, selectionSet))
        {
            found.Add(new DeliveryAnchor(cursor, index < 0 ? parentPath : parentPath.Append(index), Honored: true));
        }
        else if (keepUnhonored)
        {
            found.Add(new DeliveryAnchor(cursor, index < 0 ? parentPath : parentPath.Append(index), Honored: false));
        }
    }

    // A row is part of the client data when it belongs to the initial payload or a payload sent it.
    private static bool IsDelivered(DbRow property)
        => (property.Flags & ElementFlags.IsExcluded) != ElementFlags.IsExcluded
            || (property.DeliveryMarks & (PropertyMarks.Held | PropertyMarks.Delivered))
                == (PropertyMarks.Held | PropertyMarks.Delivered);

    /// <summary>
    /// Marks rows that a payload sent, so that no later payload writes them again.
    /// </summary>
    internal void RetireClaims(ReadOnlySpan<int> propertyRows)
    {
        foreach (var propertyRow in propertyRows)
        {
            _metaDb.OrDeliveryMarks(new Cursor(propertyRow), PropertyMarks.Retired);
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
