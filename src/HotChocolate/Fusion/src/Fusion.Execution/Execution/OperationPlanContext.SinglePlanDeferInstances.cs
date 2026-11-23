using System.Collections.Immutable;
using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Results;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Fusion.Types;

namespace HotChocolate.Fusion.Execution;

internal sealed partial class SinglePlanDeliveryState
{
    private const byte DoneFlag = 1;
    private const byte ClosedFlag = 2;
    private const byte FailedFlag = 4;
    private const byte StepFailedFlag = 8;

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

    // The errors of the rows of a group below an anchor object, by group and anchor, and the failed anchors.
    private Dictionary<long, List<GroupError>>? _groupErrors;
    private Dictionary<long, List<GroupError>>? _failureErrors;
    private HashSet<IError>? _groupOwnedErrors;
    private HashSet<long>? _failedAnchors;
    private List<GroupError>? _takenPayload;
    private List<IError>?[]? _stepErrors;
    private List<DependencyError>?[]? _dependencyErrors;
    private HashSet<int>? _walkedDependents;
    private CompositeResultDocument? _payloadDocument;
    private List<SourceError>? _sourceErrors;
    private Dictionary<long, List<IError>>? _deniedErrors;
    private List<DeniedAnswer>? _deniedAnswers;
    private ImmutableList<IError>? _sentErrors;
    private Dictionary<OperationRequirement, HashSet<int>>? _requirementReads;
    private List<CompositeResultElement>? _unwrittenRows;
    private List<CompositeResultElement>? _unwrittenOtherRows;
    private ValueCompletion? _completion;

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
    /// Determines whether the instance failed, so that it completes with errors and without data.
    /// </summary>
    public bool IsFailed(int instance) => (_instanceFlags[instance] & FailedFlag) != 0;

    /// <summary>
    /// Marks an instance of a group with a failed step as failed when a step failed while its answer was merged and
    /// the instance has unsent rows. Otherwise the rows of the instance are completed by their type.
    /// </summary>
    public void MarkFailed(int instance)
    {
        var groupId = _instanceGroups[instance];

        if (FailedWhileMerging(groupId) && _payloadDocument!.HasUnsentRows(groupId, _instanceAnchors[instance]))
        {
            _instanceFlags[instance] |= FailedFlag | StepFailedFlag;
        }
    }

    /// <summary>
    /// Determines whether a failed step of the group, or a failed step that a step of the group requires, failed
    /// the instance.
    /// </summary>
    public bool IsFailedByStep(int instance) => (_instanceFlags[instance] & StepFailedFlag) != 0;

    // A step of the group failed while its answer was merged.
    private bool FailedWhileMerging(int groupId)
    {
        foreach (var node in NodesByGroup[groupId])
        {
            if (Failed[node.Id] && FailureExceptions[node.Id] is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks the instance of a group at an anchor object as failed, also when it is announced later. The instance
    /// reports <paramref name="failure"/>, the error whose null left the group, instead of the other errors of its
    /// rows, or all of them when <paramref name="failure"/> is <c>null</c>.
    /// </summary>
    public void FailInstance(int groupId, CompositeResultDocument.Cursor anchor, GroupError? failure)
    {
        lock (_sync)
        {
            var key = CreateKey(groupId, anchor);

            (_failedAnchors ??= []).Add(key);

            if (failure is not null)
            {
                MarkFailure(key, failure);
            }
        }

        if (_instanceCount > 0 && TryGetInstance(groupId, anchor, out var instance))
        {
            _instanceFlags[instance] |= FailedFlag;
        }
    }

    /// <summary>
    /// Determines whether a step of the group failed.
    /// </summary>
    public bool IsGroupFailed(int groupId)
    {
        foreach (var node in NodesByGroup[groupId])
        {
            if (Failed[node.Id])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether every step of every group of the delivery is done, skipped or failed.
    /// </summary>
    public bool AreGroupsSettled(SelectionDelivery delivery)
    {
        foreach (var group in delivery.Groups)
        {
            if (!IsSettled(group.Id, _executionState))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Excludes an error of the result from the initial payload and from the errors that no group owns. The denied
    /// answer that holds the same error sends it.
    /// </summary>
    public void KeepWithDeniedAnswer(IError error)
    {
        lock (_sync)
        {
            (_groupOwnedErrors ??= new HashSet<IError>(ReferenceEqualityComparer.Instance)).Add(error);
        }
    }

    /// <summary>
    /// Records an error for the rows of a group below an anchor object.
    /// </summary>
    public void AddGroupError(int groupId, CompositeResultDocument.Cursor anchor, GroupError error)
    {
        lock (_sync)
        {
            _groupErrors ??= [];

            var key = CreateKey(groupId, anchor);

            if (!_groupErrors.TryGetValue(key, out var errors))
            {
                errors = _groupErrors[key] = [];
            }

            errors.Add(error);
            (_groupOwnedErrors ??= new HashSet<IError>(ReferenceEqualityComparer.Instance)).Add(error.Error);
        }
    }

    private void MarkFailure(long key, GroupError error)
    {
        _failureErrors ??= [];

        if (!_failureErrors.TryGetValue(key, out var errors))
        {
            errors = _failureErrors[key] = [];
        }

        if (!errors.Contains(error))
        {
            errors.Add(error);
        }
    }

    // The errors that the instance reports: the errors that failed it, or else all errors recorded for its rows.
    private List<GroupError>? GetReportedErrors(int instance)
    {
        var key = CreateKey(_instanceGroups[instance], _instanceAnchors[instance]);

        if (_failureErrors is not null && _failureErrors.TryGetValue(key, out var failures))
        {
            return failures;
        }

        return _groupErrors is not null && _groupErrors.TryGetValue(key, out var errors) ? errors : null;
    }

    /// <summary>
    /// Determines whether the error belongs to a delivery group instead of the initial payload.
    /// </summary>
    public bool IsGroupError(IError error)
    {
        lock (_sync)
        {
            return _groupOwnedErrors?.Contains(error) is true;
        }
    }

    /// <summary>
    /// Gets the errors without the errors that belong to a delivery group, or <c>null</c> when none is left.
    /// </summary>
    public ImmutableList<IError>? ExcludeGroupErrors(IReadOnlyList<IError>? errors)
    {
        if (errors is null)
        {
            return null;
        }

        var builder = ImmutableList.CreateBuilder<IError>();

        foreach (var error in errors)
        {
            if (!IsGroupError(error))
            {
                builder.Add(error);
            }
        }

        return builder.Count > 0 ? builder.ToImmutable() : null;
    }

    /// <summary>
    /// Takes the errors that were recorded for the rows of the instance. An error that another instance already
    /// sent is only taken when <paramref name="includeSent"/> is set.
    /// </summary>
    public ImmutableList<IError>? TakeGroupErrors(int instance, bool includeSent)
    {
        lock (_sync)
        {
            if (GetReportedErrors(instance) is not { } errors)
            {
                return null;
            }

            ImmutableList<IError>.Builder? builder = null;

            foreach (var error in errors)
            {
                if (error.Sent && !includeSent)
                {
                    continue;
                }

                if (!error.Sent)
                {
                    error.Sent = true;
                    (_takenPayload ??= []).Add(error);
                }

                (builder ??= ImmutableList.CreateBuilder<IError>()).Add(error.Error);
            }

            return builder?.ToImmutable();
        }
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the errors that are recorded belong to a failed step.
    /// </summary>
    public bool AppliesStepErrors { get; set; }

    /// <summary>
    /// Determines whether an error of a failed step was recorded for the rows of the instance.
    /// </summary>
    public bool HoldsStepError(int instance)
    {
        lock (_sync)
        {
            if (GetReportedErrors(instance) is not { } errors)
            {
                return false;
            }

            foreach (var error in errors)
            {
                if (error.IsStepError)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Records an error that a step reported for no row, for the groups of the step.
    /// </summary>
    public void AddSourceError(IError error, ImmutableArray<int> groups)
    {
        lock (_sync)
        {
            (_groupOwnedErrors ??= new HashSet<IError>(ReferenceEqualityComparer.Instance)).Add(error);
            (_sourceErrors ??= []).Add(new SourceError(new GroupError(error), groups));
        }
    }

    /// <summary>
    /// Appends the errors that the steps of the group reported for no row and that no payload sent yet.
    /// </summary>
    public ImmutableList<IError> TakeSourceErrors(int groupId, ImmutableList<IError> errors)
    {
        // The list is created and read on the loop of the delivery only.
        if (_sourceErrors is null)
        {
            return errors;
        }

        lock (_sync)
        {
            foreach (var (error, groups) in _sourceErrors)
            {
                if (error.Sent || !groups.Contains(groupId))
                {
                    continue;
                }

                error.Sent = true;
                (_takenPayload ??= []).Add(error);
                errors = errors.Add(error.Error);
            }

            return errors;
        }
    }

    /// <summary>
    /// Records the errors that a sent payload carries.
    /// </summary>
    public void AddSentErrors(ImmutableList<IError> errors)
    {
        if (errors.Count > 0)
        {
            _sentErrors = _sentErrors?.AddRange(errors) ?? errors;
        }
    }

    /// <summary>
    /// Removes the errors that have the path of an error that a sent payload carries.
    /// </summary>
    public ImmutableList<IError> ExcludeSentErrors(ImmutableList<IError> errors)
    {
        if (_sentErrors is null)
        {
            return errors;
        }

        for (var i = errors.Count - 1; i >= 0; i--)
        {
            if (errors[i].Path is { } path && IsSent(path, bottom: null))
            {
                errors = errors.RemoveAt(i);
            }
        }

        return errors;
    }

    // A sent payload carries an error at the path, or at a path on the way from it down to bottom.
    private bool IsSent(Path path, Path? bottom)
    {
        if (_sentErrors is null)
        {
            return false;
        }

        foreach (var error in _sentErrors)
        {
            if (error.Path is not { } errorPath)
            {
                continue;
            }

            if (errorPath.Equals(path))
            {
                return true;
            }

            if (bottom is null || errorPath.Length <= path.Length || errorPath.Length > bottom.Length)
            {
                continue;
            }

            var below = bottom;

            while (below.Length > errorPath.Length)
            {
                below = below.Parent;
            }

            if (!below.Equals(errorPath))
            {
                continue;
            }

            while (errorPath.Length > path.Length)
            {
                errorPath = errorPath.Parent;
            }

            if (errorPath.Equals(path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records an error that a step reported for a row of the group below an anchor object that already held a
    /// result.
    /// </summary>
    public void AddDeniedError(int groupId, CompositeResultDocument.Cursor anchor, IError error)
    {
        lock (_sync)
        {
            _deniedErrors ??= [];

            var key = CreateKey(groupId, anchor);

            if (!_deniedErrors.TryGetValue(key, out var errors))
            {
                errors = _deniedErrors[key] = [];
            }

            errors.Add(error);
        }
    }

    /// <summary>
    /// Gets the errors that steps reported for rows of the instance that already held a result, or
    /// <c>null</c> when there are none.
    /// </summary>
    public ImmutableList<IError>? GetDeniedErrors(int instance)
    {
        lock (_sync)
        {
            return _deniedErrors is not null
                && _deniedErrors.TryGetValue(
                    CreateKey(_instanceGroups[instance], _instanceAnchors[instance]),
                    out var errors)
                ? [.. errors]
                : null;
        }
    }

    /// <summary>
    /// Records that a step of <paramref name="groups"/> answered <c>null</c>, an error or a missing entity for a
    /// value that held an object or a list, or that no step had written yet.
    /// </summary>
    public void AddDeniedAnswer(
        CompositeResultDocument.Cursor container,
        ImmutableArray<int> groups,
        IError? error)
    {
        AssertOnLoop();

        (_deniedAnswers ??= []).Add(new DeniedAnswer(container, groups, error, IsWritten: false));
    }

    /// <summary>
    /// Records that a step wrote an error as the result of a value that held an object or a list, or that no step
    /// had written yet. <paramref name="groups"/> is empty for a step without delivery groups.
    /// </summary>
    public void AddWrittenAnswer(
        CompositeResultDocument.Cursor container,
        ImmutableArray<int> groups,
        IError error)
    {
        AssertOnLoop();

        (_deniedAnswers ??= []).Add(new DeniedAnswer(container, groups, error, IsWritten: true));
    }

    /// <summary>
    /// Completes the rows of the instance that no step wrote below a value for which a step of the group
    /// answered <c>null</c>, an error or a missing entity. The instance fails when such a row is non-null. A failed
    /// instance writes no row and reports the error of each answer once.
    /// </summary>
    public void CompleteDeniedAnswers(CompositeResultDocument document, int instance)
    {
        if (_deniedAnswers is null)
        {
            return;
        }

        var group = _instanceGroups[instance];
        var anchor = _instanceAnchors[instance];
        var rows = _unwrittenRows ??= [];

        for (var i = 0; i < _deniedAnswers.Count; i++)
        {
            var answer = _deniedAnswers[i];

            if (answer.IsWritten || !answer.Groups.Contains(group))
            {
                continue;
            }

            var failed = IsFailed(instance);

            if (failed && answer.Error is null)
            {
                continue;
            }

            rows.Clear();
            document.FindUnwrittenRows(answer.Container, group, anchor, settledOnly: !failed, rows);

            if (failed)
            {
                if (rows.Count > 0 && !IsSentByOpenInstance(document, answer, rows[0], instance))
                {
                    // The error has the path of the row when no group outside the answer holds the row.
                    var error = _completion!.AddUnwrittenFieldError(
                        rows[0],
                        answer.Error!,
                        document.HoldsOnlyRowsOf(rows[0], answer.Groups));
                    AddFailureError(group, anchor, new GroupError(error));
                }

                continue;
            }

            foreach (var row in rows)
            {
                _completion!.CompleteUnwrittenField(row, row.AssertSelection(), answer.Error);

                if (IsFailed(instance))
                {
                    break;
                }
            }
        }
    }

    // Records an error for the rows of a group below an anchor object as the error that failed the instance of the
    // group.
    private void AddFailureError(int groupId, CompositeResultDocument.Cursor anchor, GroupError error)
    {
        AddGroupError(groupId, anchor, error);

        lock (_sync)
        {
            MarkFailure(CreateKey(groupId, anchor), error);
        }
    }

    /// <summary>
    /// Sets the completion that completes the rows that no step wrote.
    /// </summary>
    public void AttachCompletion(ValueCompletion completion) => _completion = completion;

    /// <summary>
    /// Completes the selected rows of the instance that no step wrote and no open step can still write by their
    /// type. A non-null row propagates its <c>null</c> inside the group and fails the instance when the
    /// <c>null</c> reaches the anchor. A row that a failed step of the group left carries the error of the step, and
    /// a row whose step lacks a required value carries the error of that value, when the answer for it was denied
    /// with an error or a step that does not carry the group wrote the error, the error for a requirement of its
    /// own field first. A nullable row does not take such an error when a sent payload already carries an error of
    /// the value.
    /// </summary>
    /// <returns><c>true</c> when an open step can still write a row of the instance.</returns>
    public bool CompleteUnwrittenRows(CompositeResultDocument document, int instance, bool groupFailed)
    {
        var group = _instanceGroups[instance];
        var anchor = _instanceAnchors[instance];
        var rows = _unwrittenRows ??= [];

        rows.Clear();

        var open = document.FindUnwrittenRows(anchor, group, anchor, settledOnly: true, rows);

        if (rows.Count == 0)
        {
            return open;
        }

        var error = groupFailed ? GetStepErrors(group)?[0] ?? CreateFailureError(group) : null;

        foreach (var row in rows)
        {
            var selection = row.AssertSelection();
            var rowError = error is not null && IsLeftByFailedStep(selection, group) ? error : null;

            _completion!.CompleteUnwrittenField(
                row,
                selection,
                rowError ?? GetDeniedRequirementError(document, group, row, selection));

            if (IsFailed(instance))
            {
                break;
            }
        }

        rows.Clear();
        return open;
    }

    // Gets the error of the denied answer for a value that a requirement of the field of the row lacks. When no
    // requirement of the field lacks a value, the requirements of the step that writes the row are used.
    private IError? GetDeniedRequirementError(
        CompositeResultDocument document,
        int group,
        CompositeResultElement row,
        Selection selection)
    {
        if (!HasDeniedError(group) || !TryGetWriters(selection, out var writers))
        {
            return null;
        }

        var entity = row.Parent;

        foreach (var writer in writers)
        {
            if (!GetDeliveryGroups(writer).Contains(group))
            {
                continue;
            }

            switch (writer)
            {
                case OperationExecutionNode operation:
                    if (GetDeniedRequirementError(document, group, entity, selection, operation.Requirements)
                        is { } error)
                    {
                        return error;
                    }

                    break;

                case OperationBatchExecutionNode batch:
                    foreach (var definition in batch.Operations)
                    {
                        if (Writes(definition, selection)
                            && GetDeniedRequirementError(document, group, entity, selection, definition.Requirements)
                                is { } definitionError)
                        {
                            return definitionError;
                        }
                    }

                    break;
            }
        }

        return null;
    }

    // An answer that the fields of the group report carries an error.
    private bool HasDeniedError(int group)
    {
        if (_deniedAnswers is not null)
        {
            foreach (var answer in _deniedAnswers)
            {
                if (answer.Error is not null && answer.IsReportedBy(group))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The requirements are taken in their order in the step, the requirements of the field first. A nullable field
    // takes no error that a sent payload already carries.
    private IError? GetDeniedRequirementError(
        CompositeResultDocument document,
        int group,
        CompositeResultElement entity,
        Selection selection,
        ReadOnlySpan<OperationRequirement> requirements)
    {
        var skipSent = !selection.IsNonNull;

        for (var i = 0; i < requirements.Length; i++)
        {
            if (IsRequirementOf(requirements[i], selection)
                && GetDeniedError(document, group, entity, requirements.Slice(i, 1), skipSent) is { } error)
            {
                return error;
            }
        }

        for (var i = 0; i < requirements.Length; i++)
        {
            if (!IsRequirementOf(requirements[i], selection)
                && GetDeniedError(document, group, entity, requirements.Slice(i, 1), skipSent) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    // The requirement is declared by the field of the selection and anchored at the selection set of the selection.
    private bool IsRequirementOf(OperationRequirement requirement, Selection selection)
    {
        if (selection.Field is not FusionOutputFieldDefinition field
            || Array.IndexOf(GetTargetSelectionSets(requirement.Path), selection.DeclaringSelectionSet) < 0)
        {
            return false;
        }

        foreach (var source in field.Sources.Members)
        {
            if (source.Requirements is { } declared)
            {
                foreach (var map in declared.Fields)
                {
                    if (ReferenceEquals(map, requirement.Map))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Gets the error of the denied answer below the entity for a value that the requirement reads and that holds a
    // row without a result, taking the selection with the lowest id and, for one selection, the first recorded, a
    // denied answer before an error that a step wrote. With skipSent an answer is left out when a sent payload carries
    // an error at the path of its value, or on the way from there down to a row without a result that the requirement
    // reads.
    private IError? GetDeniedError(
        CompositeResultDocument document,
        int group,
        CompositeResultElement entity,
        ReadOnlySpan<OperationRequirement> requirement,
        bool skipSent)
    {
        var requirementReads = _requirementReads ??= new(ReferenceEqualityComparer.Instance);

        if (!requirementReads.TryGetValue(requirement[0], out var read))
        {
            read = [];
            CollectKeyAndRequirementSelectionIds(Plan.Operation, requirement, read);
            requirementReads.Add(requirement[0], read);
        }

        IError? error = null;
        var lowest = int.MaxValue;
        var isWritten = false;

        foreach (var answer in _deniedAnswers!)
        {
            if (answer.Error is null || !answer.IsReportedBy(group))
            {
                continue;
            }

            var denied = new CompositeResultElement(document, answer.Container);
            var field = denied;

            // An element of a list has the selection of the field that holds the list.
            while (field.Selection is null && !field.IsRoot)
            {
                field = field.Parent;
            }

            if (field.Selection is { } deniedSelection
                && (deniedSelection.Id < lowest || (deniedSelection.Id == lowest && isWritten && !answer.IsWritten))
                && read.Contains(deniedSelection.Id)
                && IsBelow(denied, entity)
                && LacksReadRow(denied, read)
                && !(skipSent && _sentErrors is not null && IsReadValueSent(denied, read, denied.Path)))
            {
                error = answer.Error;
                lowest = deniedSelection.Id;
                isWritten = answer.IsWritten;
            }
        }

        return error;
    }

    // A sent payload carries an error at the path of the denied value, or on the way from it down to a row without
    // a result that the requirement reads.
    private bool IsReadValueSent(CompositeResultElement value, HashSet<int> read, Path denied)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
                return IsSent(denied, value.Path);

            case JsonValueKind.Object:
                foreach (var selection in value.AssertSelectionSet().Selections)
                {
                    if (read.Contains(selection.Id)
                        && IsReadValueSent(value.GetPropertyBySelectionId(selection.Id), read, denied))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                foreach (var element in value.EnumerateArray())
                {
                    if (IsReadValueSent(element, read, denied))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    // The value is a row without a result, or holds one for a selection that the requirement reads.
    private static bool LacksReadRow(CompositeResultElement value, HashSet<int> read)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Object:
                foreach (var selection in value.AssertSelectionSet().Selections)
                {
                    if (read.Contains(selection.Id)
                        && LacksReadRow(value.GetPropertyBySelectionId(selection.Id), read))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                foreach (var element in value.EnumerateArray())
                {
                    if (LacksReadRow(element, read))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private static bool IsBelow(CompositeResultElement element, CompositeResultElement ancestor)
    {
        for (var current = element; !current.IsRoot;)
        {
            current = current.Parent;

            if (current.Cursor.Value == ancestor.Cursor.Value)
            {
                return true;
            }
        }

        return false;
    }

    // Another open instance that has not failed sends the row of the answer together with its error.
    private bool IsSentByOpenInstance(
        CompositeResultDocument document,
        DeniedAnswer answer,
        CompositeResultElement row,
        int instance)
    {
        var others = _unwrittenOtherRows ??= [];

        for (var other = 0; other < _instanceCount; other++)
        {
            if (other == instance
                || (_instanceFlags[other] & (DoneFlag | FailedFlag)) != 0
                || !answer.Groups.Contains(_instanceGroups[other]))
            {
                continue;
            }

            others.Clear();
            document.FindUnwrittenRows(
                answer.Container,
                _instanceGroups[other],
                _instanceAnchors[other],
                settledOnly: false,
                others);

            foreach (var candidate in others)
            {
                if (candidate.Cursor.Value == row.Cursor.Value)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Records the error of a failed step for the groups of the step.
    /// </summary>
    public void AddStepError(IError error, ImmutableArray<int> groups)
    {
        if (groups.IsDefaultOrEmpty)
        {
            return;
        }

        _stepErrors ??= new List<IError>?[_groupsById.Length];

        foreach (var group in groups)
        {
            var errors = _stepErrors[group] ??= [];

            if (!errors.Contains(error))
            {
                errors.Add(error);
            }
        }
    }

    /// <summary>
    /// Records the error of a failed step for the groups of the steps that require it, directly or through other
    /// steps.
    /// </summary>
    public void AddDependentStepError(IError error, ResultSelectionSet resultSelectionSet)
    {
        foreach (var node in _markedNodes)
        {
            switch (node)
            {
                case OperationExecutionNode operation
                    when ReferenceEquals(operation.ResultSelectionSet, resultSelectionSet):
                    _walkedDependents?.Clear();
                    AddDependentStepError(error, operation.Id, operation);
                    return;

                case OperationBatchExecutionNode batch:
                    foreach (var definition in batch.Operations)
                    {
                        if (ReferenceEquals(definition.ResultSelectionSet, resultSelectionSet))
                        {
                            _walkedDependents?.Clear();
                            AddDependentStepError(error, batch.Id, definition);
                            AddDependentStepError(error, batch.Id, batch);
                            return;
                        }
                    }

                    break;
            }
        }
    }

    private void AddDependentStepError(IError error, int source, IOperationPlanNode failed)
    {
        foreach (var dependent in failed.Dependents)
        {
            if (!Requires(dependent, failed) || !(_walkedDependents ??= []).Add(dependent.Id))
            {
                continue;
            }

            var groups = dependent is OperationDefinition definition
                ? definition.DeliveryGroups
                : GetDeliveryGroups(Plan.GetExecutionNode(dependent));

            if (!groups.IsDefaultOrEmpty)
            {
                _dependencyErrors ??= new List<DependencyError>?[_groupsById.Length];

                foreach (var group in groups)
                {
                    (_dependencyErrors[group] ??= []).Add(new DependencyError(error, source, dependent.Id));
                }
            }

            AddDependentStepError(error, source, dependent);
        }
    }

    private static bool Requires(IOperationPlanNode dependent, IOperationPlanNode dependency)
    {
        foreach (var required in dependent.Dependencies)
        {
            if (required.Id == dependency.Id)
            {
                return true;
            }
        }

        if (dependent is OperationExecutionNode or OperationDefinition)
        {
            foreach (var optional in dependent.OptionalDependencies)
            {
                if (optional.Id == dependency.Id
                    && !Planning.OperationPlanner.IsEntityReuseOrderingDependency(dependent, dependency))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the errors of the failed steps of the group and of the failed steps that a skipped step of the group
    /// requires, or <c>null</c> when there are none.
    /// </summary>
    public ImmutableList<IError>? GetStepErrors(int groupId)
    {
        var errors = _stepErrors?[groupId] is { Count: > 0 } own ? own.ToImmutableList() : null;

        if (_dependencyErrors?[groupId] is { } dependencyErrors)
        {
            foreach (var dependencyError in dependencyErrors)
            {
                if (Counts(dependencyError) && errors?.Contains(dependencyError.Error) is not true)
                {
                    errors = (errors ?? []).Add(dependencyError.Error);
                }
            }
        }

        return errors;
    }

    // The step that reported the error failed and the step of the group that requires it was skipped for it.
    private bool Counts(DependencyError error)
        => Failed[error.Source] && _executionState.IsNodeSkipped(error.Dependent);

    private bool HoldsDependencyFailure(int groupId)
    {
        if (_dependencyErrors?[groupId] is { } dependencyErrors)
        {
            foreach (var dependencyError in dependencyErrors)
            {
                if (Counts(dependencyError))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly record struct DependencyError(IError Error, int Source, int Dependent);

    /// <summary>
    /// Creates an error from the exception of a failed step of the group, or returns <c>null</c> when no step
    /// of the group failed with an exception.
    /// </summary>
    public IError? CreateFailureError(int groupId)
    {
        foreach (var node in NodesByGroup[groupId])
        {
            if (Failed[node.Id] && FailureExceptions[node.Id] is { } exception)
            {
                return ErrorHelper.UnexpectedExecutionError(exception);
            }
        }

        return null;
    }

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
        _takenPayload?.Clear();
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

        if (_takenPayload is not null)
        {
            foreach (var error in _takenPayload)
            {
                error.Sent = false;
            }

            _takenPayload.Clear();
        }
    }

    /// <summary>
    /// Determines whether every step that carries the group is done, skipped or failed, and whether one of
    /// them failed or requires a step that failed.
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

                if (status == 2 && HoldsDependencyFailure(groupId))
                {
                    status = 3;
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
    /// Finds an announced instance of a group at an anchor object that no payload closed and that did not fail.
    /// </summary>
    public bool TryFindOpenInstance(int groupId, CompositeResultDocument.Cursor anchor, out int instance)
        => TryGetInstance(groupId, anchor, out instance)
            && (_instanceFlags[instance] & (ClosedFlag | FailedFlag)) == 0
            && !IsGroupFailed(groupId);

    private bool TryGetInstance(int groupId, CompositeResultDocument.Cursor anchor, out int instance)
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

        return map.TryGetValue(anchor.Value, out instance);
    }

    private static long CreateKey(int groupId, CompositeResultDocument.Cursor anchor)
        => ((long)anchor.Value << 10) | (uint)groupId;

    public DeliveryClaimScratch GetClaimScratch() => _claimScratch ??= new DeliveryClaimScratch();

    /// <summary>
    /// Starts a payload and retires the rows that the previous payload claimed for an instance below the
    /// claiming one.
    /// </summary>
    public void BeginPayload(CompositeResultDocument document)
    {
        _payloadDocument = document;
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

            if (Contains(GetDelivery(selection), groupId))
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

                if (anchor.Honored && !HasNothingLeftToSend(document, groupId, anchor.Cursor))
                {
                    var instance = AddInstance(groupId, anchor.Cursor, anchor.Path!);
                    pending.Add(CreatePending(instance));
                }
                else
                {
                    // The enclosed groups of an instance that is not honored or has nothing left to send stand in its
                    // place.
                    Discover(document, groupId, anchor.Cursor, anchor.Path!, pending);
                }
            }

            _found.RemoveRange(start, _found.Count - start);
        }
    }

    // No row at or below the anchor object is unsent and no error is left to report for the instance.
    private bool HasNothingLeftToSend(
        CompositeResultDocument document,
        int groupId,
        CompositeResultDocument.Cursor anchor)
    {
        if (HoldsErrorToReport(groupId, anchor))
        {
            return false;
        }

        return !document.HasUnsentRows(groupId, anchor);
    }

    private bool HoldsErrorToReport(int groupId, CompositeResultDocument.Cursor anchor)
    {
        lock (_sync)
        {
            var key = CreateKey(groupId, anchor);

            if (_failedAnchors?.Contains(key) is true
                || _deniedErrors?.ContainsKey(key) is true)
            {
                return true;
            }

            if (_groupErrors is not null && _groupErrors.TryGetValue(key, out var errors))
            {
                foreach (var error in errors)
                {
                    if (!error.Sent)
                    {
                        return true;
                    }
                }
            }

            if (_sourceErrors is not null)
            {
                foreach (var (error, groups) in _sourceErrors)
                {
                    if (!error.Sent && groups.Contains(groupId))
                    {
                        return true;
                    }
                }
            }

            return false;
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

        lock (_sync)
        {
            if (_failedAnchors?.Contains(CreateKey(groupId, anchor)) is true)
            {
                _instanceFlags[instance] |= FailedFlag;
            }
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
/// An error that belongs to the rows of one or more delivery groups.
/// </summary>
internal sealed class GroupError(IError error)
{
    public IError Error { get; } = error;

    public bool Sent { get; set; }

    public bool IsStepError { get; init; }
}

/// <summary>
/// An error that a step reported for no row, with the delivery groups of the step.
/// </summary>
internal readonly record struct SourceError(GroupError Error, ImmutableArray<int> Groups);

/// <summary>
/// A value that held an object or a list, or that no step had written yet, when a step of the delivery groups
/// answered <c>null</c>, an error or a missing entity for it. <paramref name="IsWritten"/> is set when the step wrote
/// the error as the result of the value, and <paramref name="Groups"/> is then empty for a step without groups.
/// </summary>
internal readonly record struct DeniedAnswer(
    CompositeResultDocument.Cursor Container,
    ImmutableArray<int> Groups,
    IError? Error,
    bool IsWritten)
{
    /// <summary>
    /// Determines whether a field of the group that requires the value reports the error of the answer. That holds
    /// for a group of the step of a denied answer, and for every other group when the step wrote the error.
    /// </summary>
    public bool IsReportedBy(int group) => IsWritten != Groups.Contains(group);
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
