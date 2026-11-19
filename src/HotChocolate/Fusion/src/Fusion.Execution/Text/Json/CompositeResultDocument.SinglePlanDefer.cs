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
        var valueRow = _metaDb.Get(field.Cursor);
        var token = valueRow.TokenType;

        // A row that a failure invalidated holds a result too, so a later step does not write it.
        if (token is not ElementTokenType.None
            || (marks & PropertyMarks.Written) != 0
            || (valueRow.Flags & ElementFlags.Invalidated) == ElementFlags.Invalidated)
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
                || _delivery.IsKeyOrRequirement(selection)
                || (sourceIsContainer && !selection.IsLeaf && _delivery.IsNestedIn(resolution, deliveryGroups))))
        {
            return false;
        }

        _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Written);
        return true;
    }

    /// <summary>
    /// Determines whether the field of <paramref name="element"/> is held back for delivery groups, and gets the
    /// groups. A list item has the groups of its list.
    /// </summary>
    internal bool TryGetDestination(CompositeResultElement element, out DeliveryResolution resolution)
    {
        var current = element.Cursor;

        while (!current.IsZero)
        {
            var property = _metaDb.Get(current.AddRows(-1));

            if (property.TokenType is ElementTokenType.PropertyName)
            {
                if ((property.DeliveryMarks & PropertyMarks.Held) != 0)
                {
                    resolution = _delivery!.Resolve(_operation.GetSelectionById(property.OperationReferenceId));
                    return true;
                }

                break;
            }

            current = GetLogicalParentCursor(current);
        }

        resolution = default;
        return false;
    }

    /// <summary>
    /// Determines whether the destination of <paramref name="element"/> differs from <paramref name="other"/>.
    /// </summary>
    internal bool HasOtherDestination(CompositeResultElement element, DeliveryResolution other)
    {
        if (!TryGetDestination(element, out var resolution))
        {
            return true;
        }

        var groups = resolution.Groups;
        var otherGroups = other.Groups;

        if (groups.Length != otherGroups.Length)
        {
            return true;
        }

        foreach (var group in groups)
        {
            if (!SinglePlanDeliveryState.Contains(other, group.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fails the instances of the groups of a field at the anchor objects above the field.
    /// </summary>
    internal void FailGroups(CompositeResultElement field, DeliveryResolution groups)
    {
        var delivery = _delivery!;

        foreach (var group in groups.Groups)
        {
            if (TryGetObjectAtDepth(field.Cursor, delivery.AnchorDepths[group.Id], out var anchor))
            {
                delivery.FailInstance(group.Id, anchor);
            }
        }
    }

    /// <summary>
    /// Records an error for the delivery groups that hold the field, so that it is sent with the instances of the
    /// groups instead of the initial payload.
    /// </summary>
    /// <returns><c>true</c> when the field is held back for delivery groups.</returns>
    internal bool TryRecordGroupError(CompositeResultElement element, IError error)
    {
        var delivery = _delivery;

        if (delivery is null || !TryGetDestination(element, out var resolution))
        {
            return false;
        }

        // A row with an error holds a result, so that no later step writes it and reports its error again.
        var propertyCursor = element.Cursor.AddRows(-1);

        if (_metaDb.Get(propertyCursor).TokenType is ElementTokenType.PropertyName)
        {
            _metaDb.OrDeliveryMarks(propertyCursor, PropertyMarks.Written);
        }

        var record = new GroupError(error) { IsStepError = delivery.AppliesStepErrors };
        var recorded = false;

        foreach (var group in resolution.Groups)
        {
            if (TryGetObjectAtDepth(element.Cursor, delivery.AnchorDepths[group.Id], out var anchor))
            {
                delivery.AddGroupError(group.Id, anchor, record);
                recorded = true;
            }
        }

        return recorded;
    }

    /// <summary>
    /// Records an error that a step reported for a field that already holds a result, for the instances of the
    /// groups of the step that hold the field.
    /// </summary>
    internal void RecordDeniedError(
        CompositeResultElement element,
        ImmutableArray<int> deliveryGroups,
        IError error)
    {
        var delivery = _delivery!;

        if (!TryGetDestination(element, out var resolution))
        {
            return;
        }

        foreach (var group in resolution.Groups)
        {
            if (deliveryGroups.Contains(group.Id)
                && TryGetObjectAtDepth(element.Cursor, delivery.AnchorDepths[group.Id], out var anchor))
            {
                delivery.AddDeniedError(group.Id, anchor, error);
            }
        }
    }

    /// <summary>
    /// Determines whether every delivery group that holds the field of <paramref name="element"/> is one of
    /// <paramref name="deliveryGroups"/>.
    /// </summary>
    internal bool HoldsOnlyRowsOf(CompositeResultElement element, ImmutableArray<int> deliveryGroups)
    {
        if (!TryGetDestination(element, out var resolution))
        {
            return false;
        }

        foreach (var group in resolution.Groups)
        {
            if (!deliveryGroups.Contains(group.Id))
            {
                return false;
            }
        }

        return true;
    }

    // The anchor of a group lies as many field segments below the root as the group path has, list items not counted.
    private bool TryGetObjectAtDepth(Cursor value, int depth, out Cursor anchor)
    {
        var count = 0;
        var current = value;

        while (!current.IsZero)
        {
            current = GetLogicalParentCursor(current);

            if (IsObject(current))
            {
                count++;
            }
        }

        var skip = count - 1 - depth;
        var seen = -1;
        current = value;

        while (skip >= 0 && !current.IsZero)
        {
            current = GetLogicalParentCursor(current);

            if (IsObject(current) && ++seen == skip)
            {
                anchor = ResolveReference(current);
                return true;
            }
        }

        anchor = default;
        return false;
    }

    private bool IsObject(Cursor cursor)
    {
        if (cursor.IsZero)
        {
            return true;
        }

        var row = _metaDb.Get(cursor);

        if (row.TokenType is ElementTokenType.Reference)
        {
            row = _metaDb.Get(new Cursor(row.Location));
        }

        return row.TokenType is ElementTokenType.StartObject;
    }

    private Cursor ResolveReference(Cursor cursor)
        => !cursor.IsZero && _metaDb.Get(cursor).TokenType is ElementTokenType.Reference
            ? new Cursor(_metaDb.Get(cursor).Location)
            : cursor;

    private Cursor GetLogicalParentCursor(Cursor current)
        => GetParent(current).Cursor;

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

    // A row that no step wrote holds no result: the payload completes it by its type before it claims the rows.
    private static bool HoldsResult(int marks, DbRow valueRow, DeliveryResolution resolution, int groupId)
        => SinglePlanDeliveryState.Contains(resolution, groupId)
            && ((marks & PropertyMarks.Written) != 0 || valueRow.TokenType is not ElementTokenType.None);

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
    /// Lists the rows at and below <paramref name="container"/> that the group holds and that no step wrote, when
    /// the container lies below <paramref name="anchor"/>. With <paramref name="settledOnly"/>, a row that a step
    /// that is not settled can still write is left out.
    /// </summary>
    /// <returns><c>true</c> when a row was left out.</returns>
    internal bool FindUnwrittenRows(
        Cursor container,
        int groupId,
        Cursor anchor,
        bool settledOnly,
        List<CompositeResultElement> rows)
    {
        var delivery = _delivery!;
        delivery.AssertOnLoop();

        // The anchor object itself has no object at its own depth above it.
        return (container.Value == anchor.Value
                || (TryGetObjectAtDepth(container, delivery.AnchorDepths[groupId], out var found)
                    && found.Value == anchor.Value))
            && FindUnwrittenRows(container, _metaDb.Get(container), groupId, settledOnly, rows);
    }

    // A row that holds no result completes by its type once no step can write it any more.
    private bool FindUnwrittenRows(
        Cursor cursor,
        DbRow row,
        int groupId,
        bool settledOnly,
        List<CompositeResultElement> rows)
    {
        if (!IsContainer(ref cursor, ref row))
        {
            return false;
        }

        var open = false;

        if (row.TokenType is ElementTokenType.StartArray)
        {
            var length = row.SizeOrLength;

            for (var i = 0; i < length; i++)
            {
                var elementCursor = cursor.AddRows(i + 1);

                open |= FindUnwrittenRows(elementCursor, _metaDb.Get(elementCursor), groupId, settledOnly, rows);
            }

            return open;
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

            if ((marks & PropertyMarks.Held) != 0
                && (marks & (PropertyMarks.Written | PropertyMarks.Delivered)) == 0
                && valueRow.TokenType is ElementTokenType.None
                && (valueRow.Flags & ElementFlags.Invalidated) != ElementFlags.Invalidated)
            {
                var selection = _operation.GetSelectionById(property.OperationReferenceId);
                var resolution = _delivery!.Resolve(selection);

                if (!SinglePlanDeliveryState.Contains(resolution, groupId))
                {
                    continue;
                }

                if (settledOnly && _delivery.HasOpenWriter(selection, resolution))
                {
                    open = true;
                }
                else
                {
                    rows.Add(new CompositeResultElement(this, valueCursor));
                }

                continue;
            }

            open |= FindUnwrittenRows(valueCursor, valueRow, groupId, settledOnly, rows);
        }

        return open;
    }

    /// <summary>
    /// Determines whether a row at or below the anchor object of <paramref name="instance"/> holds no result while
    /// a step that is not settled can still write it.
    /// </summary>
    internal bool HasOpenRows(int instance)
    {
        var delivery = _delivery!;
        delivery.AssertOnLoop();

        var anchor = delivery.GetInstanceAnchor(instance);

        return HasOpenRows(anchor, _metaDb.Get(anchor), delivery.GetInstanceGroup(instance));
    }

    private bool HasOpenRows(Cursor cursor, DbRow row, int groupId)
    {
        if (!IsContainer(ref cursor, ref row))
        {
            return false;
        }

        if (row.TokenType is ElementTokenType.StartArray)
        {
            var length = row.SizeOrLength;

            for (var i = 0; i < length; i++)
            {
                var elementCursor = cursor.AddRows(i + 1);

                if (HasOpenRows(elementCursor, _metaDb.Get(elementCursor), groupId))
                {
                    return true;
                }
            }

            return false;
        }

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

            if ((marks & PropertyMarks.Held) == 0)
            {
                if ((flags & ElementFlags.IsExcluded) == ElementFlags.IsExcluded)
                {
                    continue;
                }
            }
            else if ((marks & (PropertyMarks.Written | PropertyMarks.Delivered)) == 0
                && valueRow.TokenType is ElementTokenType.None
                && (valueRow.Flags & ElementFlags.Invalidated) != ElementFlags.Invalidated)
            {
                var selection = _operation.GetSelectionById(property.OperationReferenceId);
                var resolution = _delivery!.Resolve(selection);

                if (SinglePlanDeliveryState.Contains(resolution, groupId)
                    && _delivery.HasOpenWriter(selection, resolution))
                {
                    return true;
                }

                continue;
            }

            if (HasOpenRows(valueCursor, valueRow, groupId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a row at or below the anchor object that resolves to the group is held back and was
    /// not sent in a payload.
    /// </summary>
    internal bool HasUnsentRows(int groupId, Cursor anchor)
    {
        _delivery!.AssertLoopNotRunningElsewhere();

        return HasUnsentRows(anchor, _metaDb.Get(anchor), groupId);
    }

    private bool HasUnsentRows(Cursor cursor, DbRow row, int groupId)
    {
        if (!IsContainer(ref cursor, ref row))
        {
            return false;
        }

        if (row.TokenType is ElementTokenType.StartArray)
        {
            var length = row.SizeOrLength;

            for (var i = 0; i < length; i++)
            {
                var elementCursor = cursor.AddRows(i + 1);

                if (HasUnsentRows(elementCursor, _metaDb.Get(elementCursor), groupId))
                {
                    return true;
                }
            }

            return false;
        }

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

            if ((marks & PropertyMarks.Held) == 0)
            {
                if ((flags & ElementFlags.IsExcluded) == ElementFlags.IsExcluded)
                {
                    continue;
                }
            }
            else if ((marks & PropertyMarks.Delivered) == 0)
            {
                var resolution = _delivery!.Resolve(_operation.GetSelectionById(property.OperationReferenceId));

                if (SinglePlanDeliveryState.Contains(resolution, groupId))
                {
                    return true;
                }

                continue;
            }

            var valueCursor = propertyCursor.AddRows(1);

            if (HasUnsentRows(valueCursor, _metaDb.Get(valueCursor), groupId))
            {
                return true;
            }
        }

        return false;
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
