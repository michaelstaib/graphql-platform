using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;

namespace HotChocolate.Fusion.Execution;

internal sealed partial class SinglePlanDeliveryState
{
    private const byte DoneFlag = 1;
    private const byte ClosedFlag = 2;

    private readonly DeliveryGroup[] _groupsById;
    private readonly byte[] _groupStatus;
    private readonly int[] _anchorDepths;
    private readonly Dictionary<long, bool> _selectionSetHoldsGroup = [];
    private readonly List<int> _donePayload = [];
    private readonly List<DeliveryAnchor> _found = [];
    private int[][] _effectiveChildren = [];
    private DeliveryClaimScratch? _claimScratch;
    private HashSet<long>? _crossRoots;
    private int[] _crossClaims = [];
    private int _crossClaimCount;

    // One entry per announced group instance. The pending id of an instance is its index.
    private CompositeResultDocument.Cursor[] _instanceAnchors = [];
    private Path[] _instancePaths = [];
    private int[] _instanceGroups = [];
    private byte[] _instanceFlags = [];
    private int _instanceCount;
    private int _doneCount;
    private Dictionary<int, int>?[]? _instancesByAnchor;

    /// <summary>
    /// Gets the number of group instances announced so far.
    /// </summary>
    public int InstanceCount => _instanceCount;

    /// <summary>
    /// Gets the number of announced group instances that are not completed.
    /// </summary>
    public int OpenCount => _instanceCount - _doneCount;

    /// <summary>
    /// Gets the number of field segments of the path of each delivery group, indexed by group id.
    /// </summary>
    public int[] AnchorDepths => _anchorDepths;

    public int GetInstanceGroup(int instance) => _instanceGroups[instance];

    public CompositeResultDocument.Cursor GetInstanceAnchor(int instance) => _instanceAnchors[instance];

    public bool IsDone(int instance) => (_instanceFlags[instance] & DoneFlag) != 0;

    /// <summary>
    /// Marks an instance as completed in the payload that is being created.
    /// </summary>
    public void MarkDone(int instance)
    {
        _instanceFlags[instance] |= DoneFlag;
        _doneCount++;
        _donePayload.Add(instance);
    }

    /// <summary>
    /// Closes the instances that the payload completed, so that no later payload sends data under them.
    /// </summary>
    public void EndPayload()
    {
        foreach (var instance in _donePayload)
        {
            _instanceFlags[instance] |= ClosedFlag;
        }

        _donePayload.Clear();
    }

    /// <summary>
    /// Undoes the announcements and the completions of the payload that could not be created.
    /// </summary>
    public void RollbackPayload(int instanceCountBefore)
    {
        foreach (var instance in _donePayload)
        {
            _instanceFlags[instance] &= unchecked((byte)~DoneFlag);
            _doneCount--;
        }

        _donePayload.Clear();
        _instanceCount = instanceCountBefore;
        _instancesByAnchor = null;
    }

    /// <summary>
    /// Determines whether every step that carries the group is done, skipped or failed, and whether one of
    /// them failed.
    /// </summary>
    public bool IsGroupSettled(int groupId, ExecutionState executionState, out bool failed)
    {
        ref var status = ref _groupStatus[groupId];

        if (status == 0)
        {
            status = IsSettled(groupId, executionState) ? (byte)2 : (byte)1;

            if (status == 2)
            {
                foreach (var node in NodesByGroup[groupId])
                {
                    if (Failed[node.Id])
                    {
                        status = 3;
                        break;
                    }
                }
            }
        }

        failed = status == 3;
        return status > 1;
    }

    /// <summary>
    /// Discards the group statuses that <see cref="IsGroupSettled"/> holds.
    /// </summary>
    public void ResetGroupStatus() => Array.Clear(_groupStatus);

    /// <summary>
    /// Announces the honored instances of the groups that no other honored group encloses, at the objects of
    /// the initial data.
    /// </summary>
    public void AnnounceInitial(CompositeResultDocument document, ImmutableList<PendingResult>.Builder pending)
        => Discover(
            document,
            scopeGroup: -1,
            CompositeResultDocument.Cursor.CreateZero(),
            Path.Root,
            pending);

    /// <summary>
    /// Announces the honored instances of the groups that the completed instance encloses.
    /// </summary>
    public void AnnounceChildren(
        CompositeResultDocument document,
        int instance,
        ImmutableList<PendingResult>.Builder pending)
        => Discover(
            document,
            _instanceGroups[instance],
            _instanceAnchors[instance],
            _instancePaths[instance],
            pending);

    public PendingResult CreatePending(int instance)
        => new(instance, _instancePaths[instance], _groupsById[_instanceGroups[instance]].Label);

    /// <summary>
    /// Finds an announced instance of a group at an anchor object that no payload closed.
    /// </summary>
    public bool TryFindOpenInstance(int groupId, CompositeResultDocument.Cursor anchor, out int instance)
    {
        var map = (_instancesByAnchor ??= new Dictionary<int, int>?[_groupsById.Length])[groupId];

        if (map is null)
        {
            map = _instancesByAnchor[groupId] = [];

            for (var i = 0; i < _instanceCount; i++)
            {
                if (_instanceGroups[i] == groupId)
                {
                    map[_instanceAnchors[i].Value] = i;
                }
            }
        }

        return map.TryGetValue(anchor.Value, out instance) && (_instanceFlags[instance] & ClosedFlag) == 0;
    }

    public DeliveryClaimScratch GetClaimScratch() => _claimScratch ??= new DeliveryClaimScratch();

    /// <summary>
    /// Starts a payload and retires the rows that the previous payload claimed for an instance below the
    /// claiming one.
    /// </summary>
    public void BeginPayload(CompositeResultDocument document)
    {
        document.RetireClaims(_crossClaims.AsSpan(0, _crossClaimCount));
        _crossClaimCount = 0;
        _crossRoots?.Clear();
    }

    /// <summary>
    /// Records a row that an instance claimed for an instance below it.
    /// </summary>
    public void RecordCrossClaim(CompositeResultDocument.Cursor propertyRow)
    {
        if (_crossClaimCount == _crossClaims.Length)
        {
            Array.Resize(ref _crossClaims, Math.Max(16, _crossClaimCount * 2));
        }

        _crossClaims[_crossClaimCount++] = propertyRow.Value;
    }

    /// <summary>
    /// Determines whether the payload lists the object for the group, and lists it when
    /// <paramref name="add"/> is set.
    /// </summary>
    public bool ListsObject(CompositeResultDocument.Cursor cursor, int groupId, bool add)
    {
        var key = ((long)cursor.Value << 10) | (uint)groupId;

        if (add)
        {
            return !(_crossRoots ??= []).Add(key);
        }

        return _crossRoots is { Count: > 0 } && _crossRoots.Contains(key);
    }

    /// <summary>
    /// Determines whether the selection set holds, at any depth, a selection that resolves to the group.
    /// </summary>
    public bool IsHonoredAt(int groupId, SelectionSet selectionSet)
    {
        var key = ((long)selectionSet.Id << 10) | (uint)groupId;

        if (_selectionSetHoldsGroup.TryGetValue(key, out var holds))
        {
            return holds;
        }

        holds = HoldsGroup(selectionSet, groupId);
        _selectionSetHoldsGroup[key] = holds;
        return holds;
    }

    private bool HoldsGroup(SelectionSet selectionSet, int groupId)
    {
        foreach (var selection in selectionSet.Selections)
        {
            if (selection.IsInternal)
            {
                continue;
            }

            if (Contains(Resolve(selection), groupId))
            {
                return true;
            }

            if (selection.IsLeaf)
            {
                continue;
            }

            foreach (var type in Plan.Operation.GetPossibleTypes(selection))
            {
                if (IsHonoredAt(groupId, Plan.Operation.GetSelectionSet(selection, type)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Index 0 holds the honored groups that no honored group encloses, index n + 1 the honored groups that
    // the group n encloses most closely.
    private void ComputeEffectiveChildren()
    {
        var lists = new List<int>?[_groupsById.Length + 1];

        foreach (var group in Plan.DeliveryGroups)
        {
            if (!Honored[group.Id])
            {
                continue;
            }

            var parent = -1;

            for (var ancestor = group.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (Honored[ancestor.Id])
                {
                    parent = ancestor.Id;
                    break;
                }
            }

            (lists[parent + 1] ??= []).Add(group.Id);
        }

        _effectiveChildren = new int[lists.Length][];

        for (var i = 0; i < lists.Length; i++)
        {
            _effectiveChildren[i] = lists[i]?.ToArray() ?? [];
        }
    }

    private void Discover(
        CompositeResultDocument document,
        int scopeGroup,
        CompositeResultDocument.Cursor scope,
        Path scopePath,
        ImmutableList<PendingResult>.Builder pending)
    {
        var firstSegment = scopeGroup < 0 ? 0 : (_groupsById[scopeGroup].Path?.Length ?? 0);

        foreach (var groupId in _effectiveChildren[scopeGroup + 1])
        {
            var group = _groupsById[groupId];
            var keepUnhonored = _effectiveChildren[groupId + 1].Length > 0;
            var start = _found.Count;

            document.FindDeliveryAnchors(group, firstSegment, scope, scopePath, keepUnhonored, _found);

            var end = _found.Count;

            for (var i = start; i < end; i++)
            {
                var anchor = _found[i];

                if (anchor.Honored)
                {
                    var instance = AddInstance(groupId, anchor.Cursor, anchor.Path!);
                    pending.Add(CreatePending(instance));
                }
                else
                {
                    // The enclosed groups of an instance that is not honored stand in its place.
                    Discover(document, groupId, anchor.Cursor, anchor.Path!, pending);
                }
            }

            _found.RemoveRange(start, _found.Count - start);
        }
    }

    private int AddInstance(int groupId, CompositeResultDocument.Cursor anchor, Path path)
    {
        if (_instanceCount == _instanceAnchors.Length)
        {
            var size = Math.Max(8, _instanceCount * 2);
            Array.Resize(ref _instanceAnchors, size);
            Array.Resize(ref _instancePaths, size);
            Array.Resize(ref _instanceGroups, size);
            Array.Resize(ref _instanceFlags, size);
        }

        var instance = _instanceCount++;
        _instanceAnchors[instance] = anchor;
        _instancePaths[instance] = path;
        _instanceGroups[instance] = groupId;
        _instanceFlags[instance] = 0;

        if (_instancesByAnchor?[groupId] is { } map)
        {
            map[anchor.Value] = instance;
        }

        return instance;
    }

    private static int CountFieldSegments(SelectionPath? path)
    {
        var count = 0;

        for (var i = 0; i < (path?.Length ?? 0); i++)
        {
            if (path![i].Kind is SelectionPathSegmentKind.Field)
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// An object that a delivery group is applied to.
/// </summary>
internal readonly record struct DeliveryAnchor(
    CompositeResultDocument.Cursor Cursor,
    Path? Path,
    bool Honored);

/// <summary>
/// An object of a payload that holds rows of one delivery group.
/// </summary>
internal readonly record struct ClaimedObject(
    CompositeResultDocument.Cursor Cursor,
    int Group,
    int Instance,
    Path SubPath);

/// <summary>
/// The working memory of the walks that claim the rows of group instances.
/// </summary>
internal sealed class DeliveryClaimScratch
{
    private string?[] _names = new string?[16];
    private int[] _indexes = new int[16];
    private CompositeResultDocument.Cursor[] _chain = new CompositeResultDocument.Cursor[8];
    private int[] _chainSegments = new int[8];
    private int _segmentCount;

    public int Instance { get; private set; }

    public int Group { get; private set; }

    public int BaseDepth { get; private set; }

    public List<ClaimedObject> Roots { get; private set; } = [];

    public List<(int Group, int Instance)> Carriers { get; } = [];

    public void Begin(int instance, int group, int baseDepth, List<ClaimedObject> roots)
    {
        Instance = instance;
        Group = group;
        BaseDepth = baseDepth;
        Roots = roots;
        _segmentCount = 0;
        Carriers.Clear();
    }

    public void PushName(string name)
    {
        Reserve();
        _names[_segmentCount] = name;
        _segmentCount++;
    }

    public void PushIndex(int index)
    {
        Reserve();
        _names[_segmentCount] = null;
        _indexes[_segmentCount] = index;
        _segmentCount++;
    }

    public void Pop() => _segmentCount--;

    public void EnterObject(int depth, CompositeResultDocument.Cursor cursor)
    {
        var level = depth - BaseDepth;

        if (level == _chain.Length)
        {
            Array.Resize(ref _chain, level * 2);
            Array.Resize(ref _chainSegments, level * 2);
        }

        _chain[level] = cursor;
        _chainSegments[level] = _segmentCount;
    }

    public CompositeResultDocument.Cursor GetObject(int depth) => _chain[depth - BaseDepth];

    /// <summary>
    /// Creates the path from the object at <paramref name="depth"/> to the current position.
    /// </summary>
    public Path CreatePathFrom(int depth)
    {
        var path = Path.Root;

        for (var i = _chainSegments[depth - BaseDepth]; i < _segmentCount; i++)
        {
            path = _names[i] is { } name ? path.Append(name) : path.Append(_indexes[i]);
        }

        return path;
    }

    private void Reserve()
    {
        if (_segmentCount == _names.Length)
        {
            Array.Resize(ref _names, _segmentCount * 2);
            Array.Resize(ref _indexes, _segmentCount * 2);
        }
    }
}
