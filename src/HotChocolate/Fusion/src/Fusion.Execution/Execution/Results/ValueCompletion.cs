using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;
using HotChocolate.Fusion.Types.Collections;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Execution.Results;

internal sealed class ValueCompletion
{
    private readonly FetchResultStore _store;
    private readonly int _maxDepth;
    private FusionSchemaDefinition _schema = default!;
    private IErrorHandler _errorHandler = default!;
    private ErrorHandlingMode _errorHandlingMode;
    private bool _propagateNullValues;
    private IObjectTypeDefinition? _typeMemoA;
    private IObjectTypeDefinition? _typeMemoB;
    private Dictionary<(Selection, ResultSelectionSet), (ValueTypeErrorPlacement, string[][]?)>? _errorPlacements;

    public ValueCompletion(FetchResultStore store, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _maxDepth = maxDepth;
    }

    /// <summary>
    /// Prepares the value completion for a request or subscription event
    /// that completes values against <paramref name="schema"/>.
    /// </summary>
    public void Initialize(
        FusionSchemaDefinition schema,
        IErrorHandler errorHandler,
        ErrorHandlingMode errorHandlingMode)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _schema = schema;
        _errorHandler = errorHandler;
        _errorHandlingMode = errorHandlingMode;
        _propagateNullValues = errorHandlingMode is not ErrorHandlingMode.Null;
        _typeMemoA = null;
        _typeMemoB = null;
    }

    /// <summary>
    /// Releases the request state so that the value completion can be initialized again.
    /// </summary>
    public void Clean()
    {
        _schema = default!;
        _errorHandler = default!;
        _errorHandlingMode = default;
        _propagateNullValues = false;
        _typeMemoA = null;
        _typeMemoB = null;
        _errorPlacements?.Clear();
    }

    /// <summary>
    /// Tries to complete the selection set data represented by <paramref name="target"/>.
    /// <paramref name="source"/>, checking for errors on the <paramref name="errorTrie"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the execution can continue.
    /// <c>false</c>, if the execution needs to be halted.
    /// </returns>
    public bool BuildResult(
        SourceResultElement source,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        ResultSelectionSet resultSelectionSet)
    {
        var sourceSnapshot = source.CreateSnapshot();

        if (sourceSnapshot.ValueKind is not JsonValueKind.Object)
        {
            var canExecutionContinue =
                BuildResultForInvalidSource(
                    sourceSnapshot,
                    target,
                    resultSelectionSet,
                    errorTrie);

            if (!canExecutionContinue)
            {
                return false;
            }

            return ApplyPocketedErrors(target);
        }

        if (!target.TryGetObjectContext(out var objectContext))
        {
            objectContext = InitializeTargetObject(source, target);
        }
        else if (TryUpgradeOpaqueTarget(objectContext.SelectionSet, target, source))
        {
            objectContext = target.GetObjectContext();
        }

        if (errorTrie is { Count: > 0 })
        {
            AddUnmappedSubFieldErrors(objectContext.SelectionSet, resultSelectionSet, errorTrie);
        }

        if (resultSelectionSet.HasSourceResponseNameMappings)
        {
            foreach (var property in source.EnumerateObject())
            {
                CompositeResultElement resultField;
                Selection selection;
                string sourceResponseName;

                if (resultSelectionSet.TryMapSourceResponseName(
                    property,
                    out var responseNameMapping))
                {
                    if (!objectContext.TryGetProperty(
                        responseNameMapping.ResponseNameUtf8,
                        out var mappedResultField,
                        out var mappedSelection))
                    {
                        continue;
                    }

                    resultField = mappedResultField;
                    selection = mappedSelection;
                    sourceResponseName = responseNameMapping.SourceResponseName;
                }
                else
                {
                    if (!objectContext.TryGetProperty(
                        property.NameSpan,
                        out resultField,
                        out selection))
                    {
                        continue;
                    }

                    sourceResponseName = selection.ResponseName;
                }

                var propertyValueSnapshot = property.Value.CreateSnapshot();
                var propertyValueKind = propertyValueSnapshot.ValueKind;

                if (errorTrie is null && propertyValueKind.IsScalarValue())
                {
                    if (propertyValueKind is JsonValueKind.String && selection.IsEnumValue)
                    {
                        CompleteEnumValue(
                            propertyValueSnapshot,
                            resultField,
                            Unsafe.As<FusionEnumTypeDefinition>(selection.NamedType));
                        continue;
                    }

                    resultField.SetLeafValue(propertyValueSnapshot);
                    continue;
                }

                ErrorTrie? errorTrieForResponseName = null;
                errorTrie?.TryGetValue(sourceResponseName, out errorTrieForResponseName);

                var childSet = resultSelectionSet.TryGetChild(selection.ResponseName);
                if (!TryCompleteValue(
                        propertyValueSnapshot,
                        resultField,
                        errorTrieForResponseName,
                        selection,
                        0,
                        childSet)
                    && _errorHandlingMode is ErrorHandlingMode.Propagate)
                {
                    var didPropagateToRoot = PropagateNullValues(resultField);
                    if (didPropagateToRoot)
                    {
                        return false;
                    }

                    return ApplyPocketedErrors(target);
                }
            }
        }
        else
        {
            var properties = sourceSnapshot.EnumeratePropertySnapshots();

            while (properties.MoveNext())
            {
                if (!objectContext.TryGetProperty(properties.Name, out var resultField, out var selection))
                {
                    continue;
                }

                var propertyValueSnapshot = properties.Value;
                var propertyValueKind = propertyValueSnapshot.ValueKind;

                // Fast path: when there are no errors and the source value is a
                // scalar (string, number, bool) we can set it directly without
                // going through the full TryCompleteValue type-dispatch chain.
                if (errorTrie is null && propertyValueKind.IsScalarValue())
                {
                    if (propertyValueKind is JsonValueKind.String && selection.IsEnumValue)
                    {
                        CompleteEnumValue(
                            propertyValueSnapshot,
                            resultField,
                            Unsafe.As<FusionEnumTypeDefinition>(selection.NamedType));
                        continue;
                    }

                    resultField.SetLeafValue(propertyValueSnapshot);
                    continue;
                }

                ErrorTrie? errorTrieForResponseName = null;
                errorTrie?.TryGetValue(selection.ResponseName, out errorTrieForResponseName);

                var childSet = resultSelectionSet.TryGetChild(selection.ResponseName);
                if (!TryCompleteValue(
                        propertyValueSnapshot,
                        resultField,
                        errorTrieForResponseName,
                        selection,
                        0,
                        childSet))
                {
                    switch (_errorHandlingMode)
                    {
                        case ErrorHandlingMode.Propagate:
                            var didPropagateToRoot = PropagateNullValues(resultField);
                            if (didPropagateToRoot)
                            {
                                return false;
                            }

                            return ApplyPocketedErrors(target);

                    }
                }
            }
        }

        return ApplyPocketedErrors(target);
    }

    private CompositeObjectContext InitializeTargetObject(
        SourceResultElement source,
        CompositeResultElement target)
    {
        if (!TryGetSelectionContext(target, out var selection, out var type)
            || !type.IsCompositeType())
        {
            throw new InvalidOperationException(
                "Cannot initialize a result object without selection metadata.");
        }

        var objectType = GetType(type, source, isOpaque: false);
        var objectSelectionSet = selection.GetSelectionSet(objectType)!;

        target.InitializeObjectValue(objectSelectionSet, out var objectContext);
        return objectContext;
    }

    /// <summary>
    /// When a covering lookup imports concrete data (carrying a <c>__typename</c>) into an element
    /// that is still interface-typed from an <c>@interfaceObject</c> stand-in, upgrades the element
    /// to its concrete type so the identity-dependent fields have slots to complete into.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the target was upgraded. Any object context acquired for the
    /// target before the call is then stale and must be refetched.
    /// </returns>
    private bool TryUpgradeOpaqueTarget(
        SelectionSet? targetSelectionSet,
        CompositeResultElement target,
        SourceResultElement source)
    {
        if (targetSelectionSet is not { Type.Kind: TypeKind.Interface } interfaceSet
            || interfaceSet.DeclaringSelection is not { } parentSelection)
        {
            return false;
        }

        if (!source.TryGetProperty(IntrospectionFieldNames.TypeNameSpan, out var typeName)
            || typeName.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        var concreteType = _schema.Types.GetType<IObjectTypeDefinition>(typeName.AssertString());

        if (!parentSelection.IsInternal
            && concreteType is IInaccessibleProvider { IsInaccessible: true })
        {
            _store.Result.RequireNullMarkerFinalization();
        }

        var concreteSelectionSet = parentSelection.GetSelectionSet(concreteType)!;
        _store.Result.UpgradeObject(target, concreteSelectionSet);
        return true;
    }

    /// <summary>
    /// Tries to <c>null</c> and assign the <paramref name="error"/> to the path of each field selected by
    /// <paramref name="resultSelectionSet"/> that holds no leaf value of another source, and to the sub-field that the
    /// value-type rule selects for a value-type field. When every such field holds a leaf value, the error is added
    /// once without a path.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the execution can continue.
    /// <c>false</c>, if the execution needs to be halted.
    /// </returns>
    public bool BuildErrorResult(
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet,
        IError error,
        CompactPath path)
    {
        var operation = target.Operation;
        var errorPath = path.ToPath(operation);
        _store.RecordKeyFetchErrors(errorPath, path, resultSelectionSet, error);

        // Below a null value the error is added without null propagation.
        var propagate = _store.HasReachableAncestorsUnsafe(target);
        var hasErrorField = false;
        var hasLeafValue = false;

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            // A prior field's error may have propagated a null up to this target
            // (a non-null field on a nullable parent nulls the parent). Once the
            // target is itself null or invalidated, the remaining field results
            // have nowhere to land, so stop applying them.
            if (target.IsNullOrInvalidated)
            {
                return true;
            }

            if (!target.TryGetProperty(responseName, out var fieldResult)
                || fieldResult.IsExcluded)
            {
                continue;
            }

            // A leaf that another source completed keeps its value.
            if (!fieldResult.IsInternal && HoldsLeafValue(fieldResult))
            {
                hasLeafValue = true;
                continue;
            }

            hasErrorField = true;

            if (fieldResult.IsInternal)
            {
                continue;
            }

            var selection = fieldResult.AssertSelection();
            var fieldPath = errorPath.Append(responseName);

            if (selection.IsValueTypeNamedType)
            {
                var placement = GetValueTypeErrorPlacement(
                    selection,
                    resultSelectionSet.TryGetChild(responseName),
                    fieldPath,
                    out var subFieldPaths);

                if (placement is ValueTypeErrorPlacement.None)
                {
                    continue;
                }

                if (placement is ValueTypeErrorPlacement.SubField)
                {
                    if (!propagate)
                    {
                        foreach (var subFieldPath in subFieldPaths!)
                        {
                            _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, subFieldPath)));
                        }

                        continue;
                    }

                    _store.RecordFailedValueTypeFetch(fieldResult, resultSelectionSet.TryGetChild(responseName));

                    foreach (var subFieldPath in subFieldPaths!)
                    {
                        var pocket = _store.PocketSubFieldError(
                            subFieldPath,
                            error,
                            fieldResult,
                            subFieldPath.Length - fieldPath.Length);

                        if (fieldResult.ValueKind is not JsonValueKind.Undefined && !TryApplySubFieldPocket(pocket))
                        {
                            return false;
                        }
                    }

                    continue;
                }
            }

            if (!propagate)
            {
                _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, fieldPath)));
                continue;
            }

            if (!ApplyFieldError(fieldResult, selection, error, fieldPath))
            {
                return false;
            }

            if (selection.IsValueTypeNamedType && !fieldResult.IsNullOrInvalidated)
            {
                fieldResult.SetNullValue();
            }
        }

        // An error whose fields all hold the values of other sources belongs on no field.
        if (hasLeafValue && !hasErrorField)
        {
            _store.AddError(_errorHandler.Handle(ErrorHelper.WithoutPath(error)));
        }

        return true;
    }

    /// <summary>
    /// Completes every client-visible non-null field of <paramref name="resultSelectionSet"/> that is still undefined
    /// on <paramref name="target"/> with a non-null violation at the path of the field.
    /// </summary>
    /// <returns>
    /// <c>false</c>, if the null propagated to the root.
    /// </returns>
    public bool CompleteUndefinedFields(
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet)
    {
        Path? targetPath = null;

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            if (!_store.IsReachableObjectUnsafe(target))
            {
                return true;
            }

            if (!target.TryGetProperty(responseName, out var fieldResult)
                || fieldResult.IsInternal
                || fieldResult.IsExcluded
                || fieldResult.ValueKind is not JsonValueKind.Undefined
                || fieldResult.Selection is not { IsNonNull: true } selection)
            {
                continue;
            }

            targetPath ??= target.CompactPath.ToPath(target.Operation);
            var fieldPath = targetPath.Append(responseName);

            if (!ApplyFieldError(fieldResult, selection, ErrorHelper.NonNullViolation(fieldPath), fieldPath))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the <paramref name="error"/> to the client-visible fields other than <c>__typename</c> of
    /// <paramref name="resultSelectionSet"/> that the runtime type of the target at <paramref name="targetPath"/>
    /// includes, when the target is <c>null</c> or below a <c>null</c> value.
    /// </summary>
    public void ReportErrorOnNulledTarget(
        CompactPath targetPath,
        ResultSelectionSet resultSelectionSet,
        IError error)
    {
        var errorPath = targetPath.ToPath(_store.Operation);
        _store.RecordKeyFetchErrors(errorPath, targetPath, resultSelectionSet, error);

        if (_store.GetRuntimeSelectionSetUnsafe(targetPath) is not { } selectionSet)
        {
            return;
        }

        var includeFlags = _store.IncludeFlags;

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            if (!selectionSet.TryGetSelection(responseName, out var selection)
                || selection.IsInternal
                || !selection.IsIncluded(includeFlags)
                || IsTypeNameField(selection))
            {
                continue;
            }

            var fieldPath = errorPath.Append(responseName);

            if (selection.IsValueTypeNamedType)
            {
                AddValueTypeFieldError(
                    anchor: null,
                    fieldPath,
                    selection,
                    resultSelectionSet.TryGetChild(responseName),
                    error);
                continue;
            }

            _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, fieldPath)));
        }
    }

    /// <summary>
    /// Adds the errors of <paramref name="errorTrie"/>, which the source of <paramref name="resultSelectionSet"/>
    /// raised for the target at <paramref name="targetPath"/> together with the data <paramref name="source"/>, to
    /// the client-visible fields they belong to, when the target is <c>null</c> or below a <c>null</c> value. An
    /// error below a field that the source did not fetch is added once without a path.
    /// </summary>
    public void ReportSourceErrorsOnNulledTarget(
        CompactPath targetPath,
        ResultSelectionSet resultSelectionSet,
        SourceResultElement source,
        ErrorTrie errorTrie)
    {
        if (errorTrie.Error is { } error)
        {
            ReportErrorOnNulledTarget(targetPath, resultSelectionSet, error);
            return;
        }

        AddErrorsOfUnfetchedFields(resultSelectionSet, errorTrie);
        AddFetchedFieldErrorsOnNulledTarget(targetPath, resultSelectionSet, source, errorTrie);
    }

    private void AddFetchedFieldErrorsOnNulledTarget(
        CompactPath targetPath,
        ResultSelectionSet resultSelectionSet,
        SourceResultElement source,
        ErrorTrie errorTrie)
    {
        var errorPath = targetPath.ToPath(_store.Operation);
        var selectionSet = _store.GetRuntimeSelectionSetUnsafe(targetPath);

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            var sourceResponseName = GetSourceResponseName(resultSelectionSet, responseName);

            if (!errorTrie.TryGetValue(sourceResponseName, out var fieldErrorTrie)
                || fieldErrorTrie?.FindFirstError() is not { } error)
            {
                continue;
            }

            AddFieldErrorsOnNulledTarget(
                new KeyFetchErrorOrigin(errorPath, targetPath, responseName),
                errorPath.Append(responseName),
                GetVisibleSelection(selectionSet, responseName),
                resultSelectionSet.TryGetChild(responseName),
                GetPropertyValue(source, sourceResponseName),
                fieldErrorTrie,
                error);
        }
    }

    // Adds the errors that the source raised below the fields it did not fetch once without a path.
    private void AddErrorsOfUnfetchedFields(ResultSelectionSet resultSelectionSet, ErrorTrie errorTrie)
    {
        foreach (var (key, fieldErrorTrie) in errorTrie)
        {
            if (fieldErrorTrie is not null
                && (key is not string sourceResponseName
                    || !TryGetResponseName(resultSelectionSet, sourceResponseName, out _)))
            {
                AddErrorsWithoutPath(fieldErrorTrie);
            }
        }
    }

    // Adds the errors that the source raised for the field of a nulled target, whose source value is value, to the
    // client-visible fields they belong to and records them for the dependents of the entity. An error below a value
    // the source returned that belongs on no client-visible field is added without a path.
    private void AddFieldErrorsOnNulledTarget(
        KeyFetchErrorOrigin origin,
        Path fieldPath,
        Selection? selection,
        ResultSelectionSet? fetched,
        SourceResultElement value,
        ErrorTrie fieldErrorTrie,
        IError error)
    {
        if (fieldErrorTrie.Error is not null || (fetched is null && value.ValueKind is not JsonValueKind.Object))
        {
            _store.RecordKeyFetchError(origin.EntityPath, origin.EntityCompactPath, origin.Name, fetched, error);

            if (selection is not null)
            {
                AddErrorAtField(anchor: null, fieldPath, selection, fetched, error);
            }

            return;
        }

        if (value.ValueKind is JsonValueKind.Object)
        {
            var selectionSet = selection is null ? null : GetSubFieldSelectionSet(selection);

            foreach (var (key, subFieldErrorTrie) in fieldErrorTrie)
            {
                string? responseName;

                if (subFieldErrorTrie is null)
                {
                    continue;
                }

                if (key is string sourceResponseName
                    && (fetched is null
                        ? TryGetSubField(selectionSet, responseName = sourceResponseName, out _)
                        : TryGetResponseName(fetched, sourceResponseName, out responseName)
                            && (selection is null || TryGetSubField(selectionSet, responseName, out _))))
                {
                    if (subFieldErrorTrie.FindFirstError() is { } subFieldError)
                    {
                        AddFieldErrorsOnNulledTarget(
                            origin.Append(responseName),
                            fieldPath.Append(responseName),
                            GetVisibleSelection(selectionSet, responseName),
                            fetched?.TryGetChild(responseName),
                            GetPropertyValue(value, sourceResponseName),
                            subFieldErrorTrie,
                            subFieldError);
                    }

                    continue;
                }

                AddErrorsWithoutPath(subFieldErrorTrie);
            }

            return;
        }

        foreach (var fieldError in EnumerateErrors(fieldErrorTrie))
        {
            _store.RecordKeyFetchError(origin.EntityPath, origin.EntityCompactPath, origin.Name, fetched, fieldError);
        }

        if (selection is not null)
        {
            AddErrorsBelowField(anchor: null, fieldPath, selection, fetched, fieldErrorTrie);
        }
    }

    /// <summary>
    /// Adds the <paramref name="error"/> of a source that failed for the value-type field at
    /// <paramref name="fieldPath"/>, or for the entity that holds it, after fetching <paramref name="fetched"/>.
    /// The error is added to the field, whose value held by <paramref name="anchor"/> becomes <c>null</c>, when the
    /// field has several possible types or the source fetched all of its client-visible sub-fields, otherwise to the
    /// client-visible sub-fields the source fetched that <see cref="GetErrorSubFieldPaths"/> selects, pocketed at
    /// <paramref name="anchor"/> when it is given, and nowhere when the source fetched none.
    /// </summary>
    private void AddValueTypeFieldError(
        PocketAnchor? anchor,
        Path fieldPath,
        Selection selection,
        ResultSelectionSet? fetched,
        IError error)
    {
        switch (GetValueTypeErrorPlacement(selection, fetched, fieldPath, out var subFieldPaths))
        {
            case ValueTypeErrorPlacement.Field:
                _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, fieldPath)));

                if (anchor?.Field is { IsNullOrInvalidated: false } value)
                {
                    value.SetNullValue();
                }

                break;

            case ValueTypeErrorPlacement.SubField:
                foreach (var subFieldPath in subFieldPaths!)
                {
                    AddErrorAt(anchor, subFieldPath, error);
                }

                break;
        }
    }

    // Adds the error of the client-visible field at path, whose source value is null, and the errors the source raised
    // below it, each at its own path when that path is client-visible, nowhere when it is an internal field, and
    // otherwise on the nearest client-visible field above it.
    private void AddErrorsBelowField(
        PocketAnchor? anchor,
        Path path,
        Selection selection,
        ResultSelectionSet? fetched,
        ErrorTrie errorTrie)
    {
        List<IError>? fieldErrors = null;

        if (errorTrie.Error is { } error)
        {
            (fieldErrors ??= []).Add(error);
        }

        AddSubFieldErrors(anchor, path, selection, fetched, errorTrie, ref fieldErrors);

        if (fieldErrors is not null)
        {
            foreach (var fieldError in fieldErrors)
            {
                AddErrorAtField(anchor, path, selection, fetched, fieldError);
            }
        }
    }

    // Adds the errors the source raised below the client-visible field at path like AddErrorsBelowField, and adds the
    // errors that belong on the field itself to fieldErrors.
    private void AddSubFieldErrors(
        PocketAnchor? anchor,
        Path path,
        Selection selection,
        ResultSelectionSet? fetched,
        ErrorTrie errorTrie,
        ref List<IError>? fieldErrors)
    {
        var selectionSet = selection.IsLeaf ? null : GetSubFieldSelectionSet(selection);

        foreach (var (key, childErrorTrie) in errorTrie)
        {
            string? responseName;

            if (childErrorTrie is null)
            {
                continue;
            }

            if (key is int index)
            {
                AddErrorsBelowField(anchor, path.Append(index), selection, fetched, childErrorTrie);
                continue;
            }

            // Without fetched sub-fields the source fetched the client selection set, whose names are the source names.
            if (key is string sourceResponseName
                && (fetched is null
                    ? TryGetSubField(selectionSet, responseName = sourceResponseName, out var subField)
                    : TryGetResponseName(fetched, sourceResponseName, out responseName)
                        && TryGetSubField(selectionSet, responseName, out subField)))
            {
                if (!subField.IsInternal)
                {
                    AddErrorsBelowField(
                        anchor,
                        path.Append(responseName),
                        subField,
                        fetched?.TryGetChild(responseName),
                        childErrorTrie);
                }

                continue;
            }

            AddErrors(childErrorTrie, ref fieldErrors);
        }
    }

    // Adds the error of the field at path on the sub-fields the value-type rule selects for a value-type field, or
    // nowhere when the source fetched no client-visible sub-field of it.
    private void AddErrorAtField(
        PocketAnchor? anchor,
        Path path,
        Selection selection,
        ResultSelectionSet? fetched,
        IError error)
    {
        if (selection.IsValueTypeNamedType)
        {
            switch (GetValueTypeErrorPlacement(selection, fetched, path, out var subFieldPaths))
            {
                case ValueTypeErrorPlacement.None:
                    return;

                case ValueTypeErrorPlacement.SubField:
                    foreach (var subFieldPath in subFieldPaths!)
                    {
                        AddErrorAt(anchor, subFieldPath, error);
                    }

                    return;
            }
        }

        AddErrorAt(anchor, path, error);
    }

    // Adds the error at path, or pockets it when anchor, the value-type field above path, is given.
    private void AddErrorAt(PocketAnchor? anchor, Path path, IError error)
    {
        if (anchor is { } pocketAnchor)
        {
            _store.PocketSubFieldError(path, error, pocketAnchor.Field, path.Length - pocketAnchor.PathLength);
            return;
        }

        _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, path)));
    }

    // The value-type field whose sub-field errors are pocketed, and the length of its path.
    private readonly record struct PocketAnchor(CompositeResultElement Field, int PathLength);

    // Adds each error of errorTrie once without a path.
    private void AddErrorsWithoutPath(ErrorTrie errorTrie)
    {
        foreach (var error in EnumerateErrors(errorTrie))
        {
            _store.AddError(_errorHandler.Handle(ErrorHelper.WithoutPath(error)));
        }
    }

    // Gets where the error of a source that fetched the sub-fields in fetched of a value-type field belongs: nowhere
    // when the source fetched no client-visible sub-field, on the field when the field has several possible types, is
    // a list or the source fetched every client-visible sub-field, otherwise on the client-visible sub-fields the
    // source fetched that GetErrorSubFieldPaths selects.
    private ValueTypeErrorPlacement GetValueTypeErrorPlacement(
        Selection selection,
        ResultSelectionSet? fetched,
        Path fieldPath,
        out List<Path>? subFieldPaths)
    {
        subFieldPaths = null;

        if (fetched is null)
        {
            return ValueTypeErrorPlacement.Field;
        }

        _errorPlacements ??= [];

        if (!_errorPlacements.TryGetValue((selection, fetched), out var cached))
        {
            cached = GetValueTypeErrorPlacement(selection, fetched);
            _errorPlacements.Add((selection, fetched), cached);
        }

        var (placement, subFieldNames) = cached;

        if (placement is ValueTypeErrorPlacement.SubField)
        {
            subFieldPaths = new List<Path>(subFieldNames!.Length);

            foreach (var names in subFieldNames)
            {
                var path = fieldPath;

                foreach (var name in names)
                {
                    path = path.Append(name);
                }

                subFieldPaths.Add(path);
            }
        }

        return placement;
    }

    // Gets the placement of the error of a source that fetched the sub-fields in fetched of a value-type field, and
    // the response names from the field to each sub-field that holds the error.
    private (ValueTypeErrorPlacement, string[][]?) GetValueTypeErrorPlacement(
        Selection selection,
        ResultSelectionSet fetched)
    {
        if (GetSubFieldSelectionSet(selection) is not { } selectionSet)
        {
            return (ValueTypeErrorPlacement.Field, null);
        }

        var includeFlags = _store.IncludeFlags;
        List<ErrorSubField>? subFields = null;
        CollectErrorSubFields(selectionSet, fetched, Path.Root, coverPath: null, includeFlags, ref subFields);

        if (subFields is null)
        {
            return (ValueTypeErrorPlacement.None, null);
        }

        if (selection.UnwrappedKind is TypeKind.List || FetchesEverySubField(selectionSet, fetched, includeFlags))
        {
            return (ValueTypeErrorPlacement.Field, null);
        }

        var paths = GetErrorSubFieldPaths(subFields);
        var subFieldNames = new string[paths.Count][];

        for (var i = 0; i < paths.Count; i++)
        {
            var names = new string[paths[i].Length];
            var current = paths[i];

            for (var j = names.Length - 1; j >= 0; j--)
            {
                names[j] = ((NamePathSegment)current).Name;
                current = current.Parent;
            }

            subFieldNames[i] = names;
        }

        return (ValueTypeErrorPlacement.SubField, subFieldNames);
    }

    // Gets the paths of the sub-fields that hold the error: the first sub-field that is non-null together with every
    // sub-field up to the value-type field, otherwise the first non-null sub-field and the first non-null sub-field of
    // each nullable object that the null of the sub-fields chosen before does not reach, otherwise the first sub-field.
    private static List<Path> GetErrorSubFieldPaths(List<ErrorSubField> subFields)
    {
        List<Path>? paths = null;
        List<Path>? coverPaths = null;

        foreach (var subField in subFields)
        {
            if (subField.IsNonNull && subField.CoverPath is null)
            {
                return [subField.Path];
            }
        }

        foreach (var subField in subFields)
        {
            if (!subField.IsNonNull || IsCovered(subField.Path, coverPaths))
            {
                continue;
            }

            (paths ??= []).Add(subField.Path);
            (coverPaths ??= []).Add(subField.CoverPath!);
        }

        return paths ?? [subFields[0].Path];

        static bool IsCovered(Path path, List<Path>? coverPaths)
        {
            if (coverPaths is null)
            {
                return false;
            }

            foreach (var coverPath in coverPaths)
            {
                if (PathUtilities.IsPathInSubtree(path, coverPath, includeSelf: true))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Collects the client-visible sub-fields other than __typename that the source fetched, in the order of the
    // operation and with the sub-fields of a nested value type in place of that value type. The cover path of a
    // sub-field is the nearest nullable field its null reaches, or null when its null reaches the value-type field.
    private void CollectErrorSubFields(
        SelectionSet selectionSet,
        ResultSelectionSet fetched,
        Path path,
        Path? coverPath,
        ConditionFlags includeFlags,
        ref List<ErrorSubField>? subFields)
    {
        foreach (var subField in selectionSet.Selections)
        {
            if (!subField.IsIncluded(includeFlags)
                || subField.IsInternal
                || IsTypeNameField(subField)
                || !fetched.ResponseNames.Contains(subField.ResponseName))
            {
                continue;
            }

            var fieldPath = path.Append(subField.ResponseName);
            var subFieldCoverPath = subField.IsNonNull ? coverPath : fieldPath;

            if (subField.IsValueTypeNamedType
                && subField.UnwrappedKind is not TypeKind.List
                && GetSubFieldSelectionSet(subField) is { } nestedSelectionSet
                && fetched.TryGetChild(subField.ResponseName) is { } nestedFetched)
            {
                CollectErrorSubFields(
                    nestedSelectionSet,
                    nestedFetched,
                    fieldPath,
                    subFieldCoverPath,
                    includeFlags,
                    ref subFields);
                continue;
            }

            (subFields ??= []).Add(new ErrorSubField(fieldPath, subField.IsNonNull, subFieldCoverPath));
        }
    }

    // A client-visible sub-field that a failed source fetched and the nearest nullable field its null reaches.
    private readonly record struct ErrorSubField(Path Path, bool IsNonNull, Path? CoverPath);

    /// <summary>
    /// Gets whether the null of the field at the dot separated path <paramref name="name"/>, read from
    /// <paramref name="start"/>, below the field of <paramref name="selection"/> reaches that field, because the field
    /// and every field between them are non-null.
    /// </summary>
    internal bool IsNullPropagatedTo(Selection selection, string name, int start)
    {
        while (start < name.Length)
        {
            var end = name.IndexOf('.', start);

            if (end < 0)
            {
                end = name.Length;
            }

            if (GetSubFieldSelectionSet(selection) is not { } selectionSet
                || !selectionSet.TryGetSelection(name[start..end], out var subField)
                || !subField.IsNonNull)
            {
                return false;
            }

            selection = subField;
            start = end + 1;
        }

        return true;
    }

    // Gets the selection set of the sub-fields of a value-type field that has a single possible type.
    private SelectionSet? GetSubFieldSelectionSet(Selection selection)
    {
        if (selection.NamedType is IObjectTypeDefinition objectType)
        {
            return selection.GetSelectionSet(objectType);
        }

        if (selection.IsLeaf)
        {
            return null;
        }

        var possibleTypes = _schema.GetPossibleTypes(selection.NamedType);
        return possibleTypes.Length == 1 ? selection.GetSelectionSet(possibleTypes[0]) : null;
    }

    // Gets whether the source fetched every client-visible sub-field other than __typename, including those of nested
    // value types.
    private bool FetchesEverySubField(
        SelectionSet selectionSet,
        ResultSelectionSet fetched,
        ConditionFlags includeFlags)
    {
        var fetchedNames = fetched.ResponseNames;

        foreach (var subField in selectionSet.Selections)
        {
            if (!subField.IsIncluded(includeFlags) || subField.IsInternal || IsTypeNameField(subField))
            {
                continue;
            }

            if (!fetchedNames.Contains(subField.ResponseName))
            {
                return false;
            }

            if (subField.IsValueTypeNamedType
                && GetSubFieldSelectionSet(subField) is { } nestedSelectionSet
                && fetched.TryGetChild(subField.ResponseName) is { } nestedFetched
                && !FetchesEverySubField(nestedSelectionSet, nestedFetched, includeFlags))
            {
                return false;
            }
        }

        return true;
    }

    // Gets the sub-field with the response name that is client-visible other than __typename, or internal.
    private bool TryGetSubField(
        SelectionSet? selectionSet,
        string responseName,
        [NotNullWhen(true)] out Selection? subField)
    {
        if (selectionSet is not null
            && selectionSet.TryGetSelection(responseName, out subField)
            && (subField.IsInternal || (subField.IsIncluded(_store.IncludeFlags) && !IsTypeNameField(subField))))
        {
            return true;
        }

        subField = null;
        return false;
    }

    private static void AddErrors(ErrorTrie errorTrie, ref List<IError>? errors)
    {
        foreach (var error in EnumerateErrors(errorTrie))
        {
            (errors ??= []).Add(error);
        }
    }

    private Selection? GetVisibleSelection(SelectionSet? selectionSet, string responseName)
        => selectionSet is not null
            && selectionSet.TryGetSelection(responseName, out var selection)
            && !selection.IsInternal
            && selection.IsIncluded(_store.IncludeFlags)
            && !IsTypeNameField(selection)
                ? selection
                : null;

    private static string GetSourceResponseName(ResultSelectionSet resultSelectionSet, string responseName)
        => resultSelectionSet.TryMapResponseName(responseName, out var mapping)
            ? mapping.SourceResponseName
            : responseName;

    private static bool TryGetResponseName(
        ResultSelectionSet resultSelectionSet,
        string sourceResponseName,
        [NotNullWhen(true)] out string? responseName)
    {
        foreach (var name in resultSelectionSet.ResponseNames)
        {
            if (GetSourceResponseName(resultSelectionSet, name).Equals(sourceResponseName, StringComparison.Ordinal))
            {
                responseName = name;
                return true;
            }
        }

        responseName = null;
        return false;
    }

    private static SourceResultElement GetPropertyValue(SourceResultElement value, string propertyName)
        => value.ValueKind is JsonValueKind.Object && value.TryGetProperty(propertyName, out var property)
            ? property
            : default;

    private static IEnumerable<IError> EnumerateErrors(ErrorTrie errorTrie)
    {
        var stack = new Stack<ErrorTrie>();
        stack.Push(errorTrie);

        while (stack.TryPop(out var current))
        {
            if (current.Error is { } error)
            {
                yield return error;
            }

            foreach (var child in current.Values.Reverse())
            {
                if (child is not null)
                {
                    stack.Push(child);
                }
            }
        }
    }

    private static bool IsTypeNameField(Selection selection)
        => selection.Field.Name.Equals(IntrospectionFieldNames.TypeName, StringComparison.Ordinal);

    // Records the error of the field of the target for the dependents of the entity that holds the field, with
    // the dot separated path of the field from that entity.
    private void RecordKeyFetchError(
        CompositeResultElement target,
        Selection selection,
        ResultSelectionSet? fetched,
        IError error)
    {
        var entity = target.Parent;
        var name = selection.ResponseName;

        while (!entity.IsRoot
            && entity.Parent.ValueKind is JsonValueKind.Object
            && entity.Selection is { IsValueTypeNamedType: true } valueTypeSelection)
        {
            name = valueTypeSelection.ResponseName + "." + name;
            entity = entity.Parent;
        }

        _store.RecordKeyFetchError(
            entity.Path,
            entity.CompactPath,
            name,
            selection.IsValueTypeNamedType ? fetched : null,
            error);
    }

    // The entity whose dependents receive the error of a field and the dot separated path of the field from it.
    private readonly record struct KeyFetchErrorOrigin(Path EntityPath, CompactPath EntityCompactPath, string Name)
    {
        public KeyFetchErrorOrigin Append(string responseName) => this with { Name = Name + "." + responseName };
    }

    private enum ValueTypeErrorPlacement
    {
        None,
        Field,
        SubField
    }

    /// <summary>
    /// Reports the <paramref name="error"/> of the failed field <paramref name="originName"/> like
    /// <see cref="ReportErrorOnNulledTarget"/>, unless the origin field is a client-visible non-null field.
    /// <paramref name="originName"/> is the dot separated path of the failed field from the target.
    /// </summary>
    public void ReportDependentErrorOnNulledTarget(
        CompactPath targetPath,
        ResultSelectionSet resultSelectionSet,
        string originName,
        IError error)
    {
        var separator = originName.IndexOf('.');
        var originFieldName = separator < 0 ? originName : originName[..separator];

        if (_store.GetRuntimeSelectionSetUnsafe(targetPath) is { } selectionSet
            && selectionSet.TryGetSelection(originFieldName, out var originSelection)
            && (originSelection.IsInternal || !originSelection.IsNonNull))
        {
            ReportErrorOnNulledTarget(targetPath, resultSelectionSet, error);
        }
    }

    public bool BuildErrorResult(Path path, IError error)
    {
        var reachablePath = path;

        while (!reachablePath.IsRoot)
        {
            if (_store.TryGetResult(reachablePath, out var fieldResult)
                && fieldResult.Selection is { } selection)
            {
                return ApplyFieldError(fieldResult, selection, error, path);
            }

            reachablePath = reachablePath.Parent;
        }

        var errorWithPath = ErrorBuilder.FromError(error)
            .SetPath(path)
            .Build();
        _store.AddError(_errorHandler.Handle(errorWithPath));
        return true;
    }

    public bool CompleteErrorResult(
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet)
    {
        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            if (target.IsNullOrInvalidated)
            {
                return true;
            }

            if (!target.TryGetProperty(responseName, out var fieldResult)
                || fieldResult.IsInternal
                || fieldResult.Selection is not { IsNonNull: true })
            {
                continue;
            }

            if (_errorHandlingMode is ErrorHandlingMode.Propagate
                && PropagateNullValues(fieldResult))
            {
                return false;
            }
        }

        return true;
    }

    public void FinalizePocketedErrors(CompositeResultElement resultData)
    {
        if (!_store.HasPocketedErrors)
        {
            return;
        }

        if (!resultData.IsNullOrInvalidated)
        {
            ApplyPocketedErrors(resultData);
        }

        if (!_store.HasPocketedErrors)
        {
            return;
        }

        var pockets = _store.GetSubFieldPockets();

        for (var i = 0; i < pockets?.Count; i++)
        {
            var pocket = pockets[i];

            if (pocket.IsRemoved)
            {
                continue;
            }

            var path = pocket.Path;
            var error = pocket.Error;
            _store.RemoveSubFieldPocket(pocket);

            if (!TryGetPocketedSubField(pocket, out var fieldResult, out var isBelowNullValue))
            {
                InitializeUndefinedValueTypeParent(path);

                if (!_store.TryGetResult(path, out fieldResult, out isBelowNullValue))
                {
                    _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, path)));
                    continue;
                }
            }

            if (!isBelowNullValue
                && !fieldResult.IsNullOrInvalidated
                && !fieldResult.IsInternal
                && !fieldResult.IsExcluded
                && fieldResult.Selection is { } selection)
            {
                ApplyFieldError(fieldResult, selection, error, path);
                continue;
            }

            _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, path)));
        }

        foreach (var (path, error) in _store.GetPocketedErrorsSnapshot())
        {
            if (!_store.RemovePocketedError(path))
            {
                continue;
            }

            var parentPath = path.Parent;
            var hasParent = _store.TryGetResult(parentPath, out var parentResult, out var isBelowNullValue);

            if (isBelowNullValue)
            {
                _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, parentPath)));
                _store.RemovePocketedErrorsInSubtree(parentPath);
                continue;
            }

            if (!hasParent)
            {
                continue;
            }

            if (parentResult.IsNullOrInvalidated)
            {
                _store.RemovePocketedErrorsInSubtree(parentPath);
                continue;
            }

            if (parentResult.ValueKind is JsonValueKind.Undefined)
            {
                _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, parentPath)));
                parentResult.SetNullValue();
                _store.RemovePocketedErrorsInSubtree(parentPath);
            }
        }
    }

    // Gets the sub-field of a pocket directly below its value-type field from the field the pocket holds, after
    // initializing that field like InitializeUndefinedValueTypeParent, while no value holding an object or a list was
    // replaced since the pocket was created.
    private bool TryGetPocketedSubField(
        FetchResultStore.SubFieldPocket pocket,
        out CompositeResultElement fieldResult,
        out bool isBelowNullValue)
    {
        fieldResult = default;
        isBelowNullValue = false;

        var result = _store.Result;

        if (pocket.Depth != 1
            || pocket.LayoutVersion != result.LayoutVersion
            || pocket.Path is not NamePathSegment { Name: var name })
        {
            return false;
        }

        var field = pocket.Field;
        isBelowNullValue = IsBelowNullValue(field);
        InitializeUndefinedValueTypeField(field, isBelowNullValue);

        if (pocket.LayoutVersion != result.LayoutVersion)
        {
            return false;
        }

        isBelowNullValue |= field.IsNullOrInvalidated;
        return field.ValueKind is JsonValueKind.Object && field.TryGetProperty(name, out fieldResult);

        static bool IsBelowNullValue(CompositeResultElement element)
        {
            if (element.IsRoot)
            {
                return false;
            }

            for (var current = element.Parent; ; current = current.Parent)
            {
                if (current.IsNullOrInvalidated)
                {
                    return true;
                }

                if (current.IsRoot)
                {
                    return false;
                }
            }
        }
    }

    // Initializes the undefined value-type objects above the sub-field at path with their selection sets when the
    // sources that failed for them fetched every client-visible sub-field, and otherwise sets them to null.
    private void InitializeUndefinedValueTypeParent(Path path)
    {
        var parentPath = path.Parent;

        if (parentPath.IsRoot)
        {
            return;
        }

        if (!_store.TryGetResult(parentPath, out var parent, out var isBelowNullValue))
        {
            InitializeUndefinedValueTypeParent(parentPath);

            if (!_store.TryGetResult(parentPath, out parent, out isBelowNullValue))
            {
                return;
            }
        }

        InitializeUndefinedValueTypeField(parent, isBelowNullValue);
    }

    // Initializes the undefined value-type field with its selection set when the sources that failed for it fetched
    // every client-visible sub-field, and otherwise sets it to null.
    private void InitializeUndefinedValueTypeField(CompositeResultElement parent, bool isBelowNullValue)
    {
        if (isBelowNullValue
            || parent.ValueKind is not JsonValueKind.Undefined
            || parent.Selection is not { IsValueTypeNamedType: true } selection)
        {
            return;
        }

        if (GetSubFieldSelectionSet(selection) is { } selectionSet
            && IsFetchedByFailedSources(parent, selectionSet))
        {
            parent.SetObjectValue(selectionSet);
            return;
        }

        if (_errorHandlingMode is ErrorHandlingMode.Propagate && selection.IsNonNull)
        {
            PropagateNullValues(parent);
        }
        else
        {
            parent.SetNullValue();
        }
    }

    // Gets whether the sources that failed for the value-type field, or for a value-type field above it, fetched
    // every client-visible sub-field of selectionSet.
    private bool IsFetchedByFailedSources(CompositeResultElement field, SelectionSet selectionSet)
    {
        var includeFlags = _store.IncludeFlags;

        foreach (var subField in selectionSet.Selections)
        {
            if (subField.IsIncluded(includeFlags)
                && !subField.IsInternal
                && !IsTypeNameField(subField)
                && !IsFetchedByFailedSource(field, subField.ResponseName))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsFetchedByFailedSource(CompositeResultElement field, string responseName)
    {
        List<string>? namesBelow = null;

        for (var current = field;
            !current.IsRoot && current.Selection is { IsValueTypeNamedType: true } selection;
            current = current.Parent)
        {
            if (_store.TryGetFailedValueTypeFetches(current, out var fetches))
            {
                foreach (var fetched in fetches)
                {
                    if (fetched is null
                        || GetFetchedBelow(fetched, namesBelow)?.ResponseNames.Contains(responseName) == true)
                    {
                        return true;
                    }
                }
            }

            (namesBelow ??= []).Add(selection.ResponseName);
        }

        return false;
    }

    // Gets the fetched sub-fields at the response names below the field, which are listed from the innermost one.
    private static ResultSelectionSet? GetFetchedBelow(ResultSelectionSet fetched, List<string>? namesBelow)
    {
        var child = fetched;

        for (var i = (namesBelow?.Count ?? 0) - 1; i >= 0; i--)
        {
            if (child.TryGetChild(namesBelow![i]) is not { } next)
            {
                return null;
            }

            child = next;
        }

        return child;
    }

    public void FinalizeInaccessibleRuntimeTypes(CompositeResultElement resultData)
    {
        if (!_store.Result.RequiresNullMarkerFinalization)
        {
            return;
        }

        Visit(resultData, isPublic: true);
        _store.Result.CompleteNullMarkerFinalization();

        static void Visit(CompositeResultElement current, bool isPublic)
        {
            if (!isPublic || current.IsNullOrInvalidated || current.IsNullMarker)
            {
                return;
            }

            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    if (isPublic
                        && current.Type?.NamedType().IsAbstractType() == true
                        && current.SelectionSet?.Type is IInaccessibleProvider { IsInaccessible: true })
                    {
                        SetNullMarker(current);
                        return;
                    }

                    foreach (var property in current.EnumerateObject())
                    {
                        Visit(property.Value, !property.Value.IsInternal);
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (var element in current.EnumerateArray())
                    {
                        Visit(element, isPublic);
                    }
                    break;
            }
        }
    }

    private static void SetNullMarker(CompositeResultElement result)
    {
        var current = result;

        while (!current.IsRoot && !current.IsNullable)
        {
            current = current.Parent;
        }

        current.SetNullMarker();
    }

    /// <summary>
    /// Invalidates the current result and its parents,
    /// until reaching a parent that can be set to <c>null</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the null propagated up to the root.
    /// </returns>
    private static bool PropagateNullValues(CompositeResultElement result)
    {
        var current = result;

        do
        {
            if (current.IsNullable)
            {
                current.SetNullValue();
                return false;
            }

            current.Invalidate();
            current = current.Parent;
        } while (!current.IsNullOrInvalidated);

        return true;
    }

    private bool BuildResultForInvalidSource(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet,
        ErrorTrie? errorTrie)
    {
        var sourceValueKind = source.ValueKind;
        var error = errorTrie?.FindFirstError();

        if (sourceValueKind is JsonValueKind.Null && IsValueType(target.Type))
        {
            if (error is not null)
            {
                PocketErrors(target.Path, resultSelectionSet, error);
            }

            return true;
        }

        // A clean null source without an associated error is a legitimate
        // "not found" state (e.g. a lookup that resolved to null), so each
        // selected field is completed with its own nullability semantics
        // instead of surfacing an unexpected execution error.
        if (sourceValueKind is JsonValueKind.Null && error is null)
        {
            return CompleteNullSource(source, target, resultSelectionSet, errorTrie: null);
        }

        // A null source whose errors were raised below it reports each error at the field that raised it.
        if (sourceValueKind is JsonValueKind.Null
            && errorTrie is { Error: null }
            && (target.ValueKind is JsonValueKind.Object
                || (target.ValueKind is JsonValueKind.Undefined && TryInitializeTargetObject(target))))
        {
            return CompleteNullSourceWithErrors(source, target, resultSelectionSet, errorTrie);
        }

        var fallbackError = error ??
            ErrorBuilder.New()
                .SetMessage("Unexpected Execution Error")
                .Build();

        if (target.ValueKind is JsonValueKind.Undefined
            && !TryInitializeTargetObject(target))
        {
            return BuildErrorResultForUndefinedTarget(
                target,
                resultSelectionSet,
                fallbackError,
                target.CompactPath);
        }

        return BuildErrorResult(target, resultSelectionSet, fallbackError, target.CompactPath);
    }

    /// <summary>
    /// Completes the selection set of <paramref name="target"/> for a <c>null</c>
    /// source, applying per-field nullability so that non-null fields produce a
    /// non-null violation and nullable fields are set to <c>null</c>. Fields that hold a
    /// leaf value and fields with errors in <paramref name="errorTrie"/> are skipped.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the execution can continue.
    /// <c>false</c>, if the execution needs to be halted.
    /// </returns>
    private bool CompleteNullSource(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet,
        ErrorTrie? errorTrie)
    {
        if (target.ValueKind is JsonValueKind.Undefined
            && !TryInitializeTargetObject(target))
        {
            return true;
        }

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            if (!target.TryGetProperty(responseName, out var fieldResult)
                || fieldResult.IsInternal
                || HoldsLeafValue(fieldResult)
                || TryGetFieldErrorTrie(errorTrie, resultSelectionSet, responseName, out _))
            {
                continue;
            }

            var selection = fieldResult.AssertSelection();
            var childSet = resultSelectionSet.TryGetChild(selection.ResponseName);

            if (!TryCompleteValue(
                    source,
                    fieldResult,
                    errorTrie: null,
                    selection,
                    0,
                    childSet))
            {
                switch (_errorHandlingMode)
                {
                    case ErrorHandlingMode.Propagate:
                        var didPropagateToRoot = PropagateNullValues(fieldResult);
                        if (didPropagateToRoot)
                        {
                            return false;
                        }

                        return ApplyPocketedErrors(target);
                }
            }
        }

        return ApplyPocketedErrors(target);
    }

    /// <summary>
    /// Completes the selection set of the object <paramref name="target"/> for a <c>null</c> source like
    /// <see cref="CompleteNullSource"/>, and adds each error of <paramref name="errorTrie"/> once at the path of the
    /// field that raised it, with the null propagating from that field.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the execution can continue.
    /// <c>false</c>, if the execution needs to be halted.
    /// </returns>
    private bool CompleteNullSourceWithErrors(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet,
        ErrorTrie errorTrie)
    {
        var targetPath = target.CompactPath.ToPath(target.Operation);
        var isReachable = _store.HasReachableAncestorsUnsafe(target);
        var canExecutionContinue = true;

        // The null of the source makes every field it fetched missing for the dependents of the entity.
        foreach (var error in EnumerateErrors(errorTrie))
        {
            _store.RecordKeyFetchErrors(targetPath, target.CompactPath, resultSelectionSet, error);
        }

        AddErrorsOfUnfetchedFields(resultSelectionSet, errorTrie);

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            if (!TryGetFieldErrorTrie(errorTrie, resultSelectionSet, responseName, out var fieldErrorTrie))
            {
                continue;
            }

            if (!target.TryGetProperty(responseName, out var fieldResult)
                || fieldResult.IsInternal
                || fieldResult.IsExcluded)
            {
                continue;
            }

            if (HoldsLeafValue(fieldResult))
            {
                AddErrorsWithoutPath(fieldErrorTrie);
                continue;
            }

            var selection = fieldResult.AssertSelection();
            var childSet = resultSelectionSet.TryGetChild(responseName);

            // Once the null propagated past the target, the errors of the other fields are added below that null.
            if (!canExecutionContinue || !isReachable || target.IsNullOrInvalidated)
            {
                if (fieldErrorTrie.FindFirstError() is { } fieldError)
                {
                    AddFieldErrorsOnNulledTarget(
                        new KeyFetchErrorOrigin(targetPath, target.CompactPath, responseName),
                        targetPath.Append(responseName),
                        selection,
                        childSet,
                        default,
                        fieldErrorTrie,
                        fieldError);
                }

                continue;
            }

            // A value-type field keeps its value for the other sources that fetch it, unless the value-type rule
            // puts the error on the field, which makes it null.
            if (selection.IsValueTypeNamedType)
            {
                AddNullValueTypeFieldErrors(fieldResult, fieldErrorTrie, selection, childSet);

                if (selection.IsNonNull
                    && fieldResult.ValueKind is JsonValueKind.Null
                    && _errorHandlingMode is ErrorHandlingMode.Propagate
                    && PropagateNullValues(target))
                {
                    canExecutionContinue = false;
                }

                continue;
            }

            AddErrorsBelowField(anchor: null, targetPath.Append(responseName), selection, childSet, fieldErrorTrie);

            if (_errorHandlingMode is ErrorHandlingMode.Propagate && selection.IsNonNull)
            {
                if (PropagateNullValues(fieldResult))
                {
                    canExecutionContinue = false;
                }
            }
            else if (fieldResult.ValueKind is JsonValueKind.Undefined)
            {
                fieldResult.SetNullValue();
            }
        }

        if (!canExecutionContinue)
        {
            return false;
        }

        if (!isReachable || target.IsNullOrInvalidated)
        {
            return true;
        }

        return CompleteNullSource(source, target, resultSelectionSet, errorTrie);
    }

    // Gets the errors that the source raised for the field with the response name, or below it.
    private static bool TryGetFieldErrorTrie(
        ErrorTrie? errorTrie,
        ResultSelectionSet resultSelectionSet,
        string responseName,
        [NotNullWhen(true)] out ErrorTrie? fieldErrorTrie)
    {
        if (errorTrie is not null
            && errorTrie.TryGetValue(GetSourceResponseName(resultSelectionSet, responseName), out fieldErrorTrie)
            && fieldErrorTrie is not null)
        {
            return true;
        }

        fieldErrorTrie = null;
        return false;
    }

    // Gets whether the field holds a leaf value that another source completed.
    private static bool HoldsLeafValue(CompositeResultElement fieldResult)
        => fieldResult.ValueKind
            is JsonValueKind.String
            or JsonValueKind.Number
            or JsonValueKind.True
            or JsonValueKind.False;

    private static bool TryInitializeTargetObject(CompositeResultElement target)
    {
        if (!TryGetSelectionContext(target, out var selection, out var type)
            || type.NamedType() is not IComplexTypeDefinition complexType)
        {
            return false;
        }

        if (selection.IsLeaf)
        {
            return false;
        }

        target.SetObjectValue(selection.GetSelectionSet(complexType)!);
        return true;
    }

    private static bool TryGetSelectionContext(
        CompositeResultElement target,
        [NotNullWhen(true)] out Selection? selection,
        [NotNullWhen(true)] out IType? type)
    {
        // Fast path: a direct-field target carries its own selection on the
        // preceding property row. Reading that selection once lets us derive
        // the type from it, avoiding a second metadb read via the Type getter.
        selection = target.Selection;

        if (selection is not null)
        {
            type = selection.Type;
            return true;
        }

        type = target.Type;

        if (type is null)
        {
            selection = null;
            return false;
        }

        var current = target;
        while ((selection = current.Selection) is null)
        {
            var parent = current.Parent;
            if (parent.IsNullOrInvalidated)
            {
                type = null;
                return false;
            }

            current = parent;
        }

        return true;
    }

    private bool BuildErrorResultForUndefinedTarget(
        CompositeResultElement target,
        ResultSelectionSet resultSelectionSet,
        IError error,
        CompactPath path)
    {
        var operation = target.Operation;
        var errorPath = path.ToPath(operation);
        var hasResponseNames = false;

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            hasResponseNames = true;
            var errorWithPath = ErrorBuilder.FromError(error)
                .SetPath(errorPath.Append(responseName))
                .Build();

            _store.AddError(_errorHandler.Handle(errorWithPath));
        }

        if (!hasResponseNames)
        {
            var errorWithPath = ErrorBuilder.FromError(error)
                .SetPath(errorPath)
                .Build();

            _store.AddError(_errorHandler.Handle(errorWithPath));
        }

        return true;
    }

    private bool ApplyPocketedErrors(CompositeResultElement target)
    {
        if (!_store.HasPocketedErrors)
        {
            return true;
        }

        if (_store.TryGetSubFieldPockets(target, out var pockets))
        {
            for (var i = 0; i < pockets.Count; i++)
            {
                var pocket = pockets[i];

                // A sub-field pocket can only be applied once its value-type field holds a value.
                if (!pocket.IsRemoved
                    && pocket.Field.ValueKind is not JsonValueKind.Undefined
                    && !TryApplySubFieldPocket(pocket))
                {
                    return false;
                }
            }

            _store.CompactSubFieldPocketsAt(target);
        }

        if (_store.HasPathPocketedErrors)
        {
            var targetPath = target.Path;

            if (_store.TryGetPocketedErrorPaths(targetPath, out var paths))
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    if (_store.TryGetPocketedError(paths[i], out var error)
                        && !TryApplyPocketedError(paths[i], error))
                    {
                        return false;
                    }
                }

                _store.CompactPocketedErrorPathsAt(targetPath);
            }
        }

        return true;
    }

    private bool TryApplySubFieldPocket(FetchResultStore.SubFieldPocket pocket)
    {
        var path = pocket.Path;
        var error = pocket.Error;

        if (pocket.IsRemoved || !_store.TryGetResult(path, out var fieldResult))
        {
            return true;
        }

        _store.RemoveSubFieldPocket(pocket);

        if (fieldResult.IsNullOrInvalidated || fieldResult.Selection is not { } selection)
        {
            _store.AddError(_errorHandler.Handle(ErrorHelper.WithPath(error, path)));
            return true;
        }

        if (fieldResult.IsInternal || fieldResult.IsExcluded)
        {
            return true;
        }

        return ApplyFieldError(fieldResult, selection, error, path);
    }

    private bool TryApplyPocketedError(Path path, IError error)
    {
        if (!_store.TryGetResult(path, out var fieldResult))
        {
            return true;
        }

        if (fieldResult.IsNullOrInvalidated)
        {
            _store.RemovePocketedError(path);
            return true;
        }

        if (fieldResult.Selection is not { } selection)
        {
            _store.RemovePocketedError(path);
            return true;
        }

        var parent = fieldResult.Parent;

        if (fieldResult.IsInternal || fieldResult.IsExcluded)
        {
            if (fieldResult.IsInternal)
            {
                _store.RecordPocketedKeyFetchError(path, parent.CompactPath, selection.ResponseName, error);
            }

            _store.RemovePocketedError(path);
            return true;
        }

        var canExecutionContinue = ApplyFieldError(fieldResult, selection, error, path);
        _store.RemovePocketedError(path);

        if (parent.IsNullOrInvalidated)
        {
            _store.RemovePocketedErrorsInSubtree(path.Parent);
        }

        return canExecutionContinue;
    }

    private bool ApplyFieldError(
        CompositeResultElement fieldResult,
        Selection selection,
        IError error,
        Path path)
    {
        var errorWithPath = ErrorBuilder.FromError(error)
            .SetPath(path)
            .Build();
        errorWithPath = _errorHandler.Handle(errorWithPath);

        _store.AddError(errorWithPath);

        switch (_errorHandlingMode)
        {
            case ErrorHandlingMode.Propagate when selection.IsNonNull:
                var didPropagateToRoot = PropagateNullValues(fieldResult);
                return !didPropagateToRoot;
        }

        return true;
    }

    private void PocketErrors(Path path, ResultSelectionSet resultSelectionSet, IError error)
    {
        var hasEntity = _store.TryGetResult(path, out var entity) && entity.ValueKind is JsonValueKind.Object;

        foreach (var responseName in resultSelectionSet.ResponseNames)
        {
            if (hasEntity && entity.TryGetProperty(responseName, out var field))
            {
                if (field.IsInternal)
                {
                    _store.RecordKeyFetchError(
                        path,
                        entity.CompactPath,
                        responseName,
                        resultSelectionSet.TryGetChild(responseName),
                        error);
                    continue;
                }

                if (field.IsExcluded)
                {
                    continue;
                }
            }

            _store.PocketError(path.Append(responseName), error, resultSelectionSet);
        }
    }

    private static bool IsValueType(IType? type)
    {
        if (type is null)
        {
            return false;
        }

        var namedType = type.NamedType();

        return namedType switch
        {
            FusionObjectTypeDefinition { IsValueType: true } => true,
            FusionInterfaceTypeDefinition { IsValueType: true } => true,
            FusionUnionTypeDefinition { IsValueType: true } => true,
            _ => false
        };
    }

    /// <summary>
    /// Completes a source value against the field type of <paramref name="selection"/>.
    /// The type shape is read from facts precomputed on the selection, so a call
    /// site completing a value against any other type must not use this method.
    /// </summary>
    private bool TryCompleteValue(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        Selection selection,
        int depth,
        ResultSelectionSet? resultSelectionSet)
    {
        var sourceValueKind = source.ValueKind;

        if (sourceValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return TryCompleteNullValue(
                sourceValueKind,
                target,
                errorTrie,
                selection,
                resultSelectionSet);
        }

        // An error on a field that still has a value is forwarded without nulling the value.
        if (errorTrie?.Error is { } fieldError)
        {
            AddFieldError(fieldError, target);
        }

        switch (selection.UnwrappedKind)
        {
            case TypeKind.List:
                // The element shape of the first list level is precomputed on the
                // selection, so it is guaranteed to be present here.
                return TryCompleteList(
                    source,
                    target,
                    errorTrie,
                    selection,
                    selection.ListElementType!,
                    selection.ListElementKind,
                    selection.IsNonNullListElement,
                    depth,
                    resultSelectionSet);

            case TypeKind.Object:
                return TryCompleteObjectValue(
                    selection,
                    source,
                    errorTrie,
                    depth,
                    target,
                    resultSelectionSet);

            case TypeKind.Interface or TypeKind.Union:
                return TryCompleteAbstractValue(
                    source,
                    target,
                    errorTrie,
                    selection,
                    depth,
                    resultSelectionSet);

            case TypeKind.Scalar:
                target.SetLeafValue(source);
                return true;

            case TypeKind.Enum:
                CompleteEnumValue(source, target, selection);
                return true;

            default:
                throw ThrowHelper.ValueCompletionTypeNotSupported(selection.UnwrappedType);
        }
    }

    /// <summary>
    /// Completes a <c>null</c> or undefined source value against <paramref name="selection"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the execution can continue.
    /// <c>false</c>, if the null must propagate to the parent.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryCompleteNullValue(
        JsonValueKind sourceValueKind,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        Selection selection,
        ResultSelectionSet? resultSelectionSet)
    {
        if (selection.IsNonNull)
        {
            IError error;
            if (errorTrie?.FindFirstError() is { } errorFromPath)
            {
                var path = target.CompactPath.ToPath(target.Operation);
                error = ErrorBuilder.FromError(errorFromPath)
                    .SetPath(path)
                    .Build();
            }
            else
            {
                error = ErrorHelper.NonNullViolation(target.CompactPath.ToPath(target.Operation));
            }

            error = _errorHandler.Handle(error);

            _store.AddError(error);

            return !_propagateNullValues;
        }

        if (sourceValueKind is JsonValueKind.Null && selection.IsValueTypeNamedType)
        {
            if (errorTrie is not null)
            {
                AddNullValueTypeFieldErrors(target, errorTrie, selection, resultSelectionSet);
            }

            // For shared parent types we keep the target untouched so that
            // sibling subgraph results can still initialize and populate it.
            return true;
        }

        // If the value is null, it might've been nulled due to a
        // down-stream null propagation.
        // So we try to get an error that is associated with this field
        // or with a path below it.
        if (errorTrie?.FindFirstError() is { } nullErrorFromPath)
        {
            RecordKeyFetchError(target, selection, resultSelectionSet, nullErrorFromPath);

            if (selection.IsInternal)
            {
                return true;
            }

            var errorWithPath = ErrorBuilder.FromError(nullErrorFromPath)
                .SetPath(target.Path)
                .Build();
            errorWithPath = _errorHandler.Handle(errorWithPath);

            _store.AddError(errorWithPath);
        }
        else
        {
            target.SetNullValue();
        }

        return true;
    }

    // Adds the errors of a source that returned null for the value-type field of the target, after fetching the
    // sub-fields in resultSelectionSet, by the value-type rule, and records them for the dependents of the entity.
    private void AddNullValueTypeFieldErrors(
        CompositeResultElement target,
        ErrorTrie errorTrie,
        Selection selection,
        ResultSelectionSet? resultSelectionSet)
    {
        if (errorTrie.FindFirstError() is not { } error)
        {
            return;
        }

        foreach (var fieldError in EnumerateErrors(errorTrie))
        {
            RecordKeyFetchError(target, selection, resultSelectionSet, fieldError);
        }

        if (selection.IsInternal || target.IsExcluded)
        {
            return;
        }

        var path = target.Path;
        var anchor = new PocketAnchor(target, path.Length);
        List<IError>? fieldErrors = null;
        _store.RecordFailedValueTypeFetch(target, resultSelectionSet);

        if (errorTrie.Error is not null)
        {
            AddValueTypeFieldError(anchor, path, selection, resultSelectionSet, error);
        }

        AddSubFieldErrors(anchor, path, selection, resultSelectionSet, errorTrie, ref fieldErrors);

        if (fieldErrors is not null)
        {
            foreach (var fieldError in fieldErrors)
            {
                AddValueTypeFieldError(anchor, path, selection, resultSelectionSet, fieldError);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddFieldError(IError fieldError, CompositeResultElement target)
    {
        var errorWithPath = ErrorBuilder.FromError(fieldError)
            .SetPath(target.Path)
            .Build();
        errorWithPath = _errorHandler.Handle(errorWithPath);

        _store.AddError(errorWithPath);
    }

    /// <summary>
    /// Completes the elements of a nested list. The element shape of nested list
    /// levels is not precomputed on the selection, so it is derived from
    /// <paramref name="type"/> per invocation.
    /// </summary>
    private bool TryCompleteNestedList(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        Selection selection,
        IType type,
        int depth,
        ResultSelectionSet? resultSelectionSet)
    {
        var elementType = type.ElementType();
        var elementTypeKind = elementType.Kind;
        var isNonNull = elementTypeKind is TypeKind.NonNull;

        if (isNonNull)
        {
            elementTypeKind = Unsafe.As<IType, NonNullType>(ref elementType).NullableType.Kind;
        }

        return TryCompleteList(
            source,
            target,
            errorTrie,
            selection,
            elementType,
            elementTypeKind,
            isNonNull,
            depth,
            resultSelectionSet);
    }

    private bool TryCompleteList(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        Selection selection,
        IType elementType,
        TypeKind elementTypeKind,
        bool isNonNull,
        int depth,
        ResultSelectionSet? resultSelectionSet)
    {
        AssertDepthAllowed(ref depth);

        if (target.IsNullOrInvalidated)
        {
            return true;
        }

        // A shared list slot may already be populated by a sibling subgraph
        // result. Create the array only on the first write; otherwise reuse it
        // so sibling contributions accumulate through the positional merge below.
        CompositeResultElement.ArrayEnumerator targetEnumerator;

        if (target.ValueKind is JsonValueKind.Undefined)
        {
            targetEnumerator = target.InitializeArrayValue(source.GetArrayLength());
        }
        else if (target.ValueKind is not JsonValueKind.Array
            || target.GetArrayLength() != source.GetArrayLength())
        {
            return ReportSharedListLengthMismatch(target);
        }
        else
        {
            targetEnumerator = target.EnumerateArray().GetEnumerator();
        }

        var listObjectType = elementTypeKind is TypeKind.Object
            ? (IComplexTypeDefinition)selection.NamedType
            : null;
        SelectionSet? listObjectSelectionSet = null;
        var i = 0;
        var elements = source.EnumerateElementSnapshots();

        while (elements.MoveNext())
        {
            var success = targetEnumerator.MoveNext();
            Debug.Assert(success, "The lists must have the same size.");

            ErrorTrie? errorTrieForIndex = null;

            if (errorTrie is not null)
            {
                errorTrieForIndex = GetListElementErrorTrie(errorTrie, target, i);
            }

            var elementSnapshot = elements.Current;
            var elementValueKind = elementSnapshot.ValueKind;
            if (elementValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                // The element might have been nulled due to a down-stream null propagation,
                // so the errors below it are forwarded with their paths.
                if (errorTrieForIndex is { Count: > 0 })
                {
                    ForwardListElementErrors(errorTrieForIndex, target, i, elementType, selection);
                }

                if (isNonNull && _propagateNullValues)
                {
                    return false;
                }

                targetEnumerator.Current.SetNullValue();
                goto TryCompleteList_MoveNext;
            }

            var targetElement = targetEnumerator.Current;
            bool completed;

            switch (elementTypeKind)
            {
                case TypeKind.List:
                    completed = TryCompleteNestedList(
                        elementSnapshot,
                        targetElement,
                        errorTrieForIndex,
                        selection,
                        elementType,
                        depth,
                        resultSelectionSet);
                    break;

                case TypeKind.Scalar:
                    targetElement.SetLeafValue(elementSnapshot);
                    completed = true;
                    break;

                case TypeKind.Enum:
                    CompleteEnumValue(elementSnapshot, targetElement, selection);
                    completed = true;
                    break;

                case TypeKind.Interface or TypeKind.Union:
                    completed = TryCompleteAbstractValue(
                        elementSnapshot,
                        targetElement,
                        errorTrieForIndex,
                        selection,
                        depth,
                        resultSelectionSet);
                    break;

                default:
                    listObjectSelectionSet ??= selection.GetSelectionSet(listObjectType!);
                    completed = TryCompleteObjectValue(
                        elementSnapshot,
                        targetElement,
                        errorTrieForIndex,
                        selection,
                        listObjectType!,
                        depth,
                        resultSelectionSet,
                        listObjectSelectionSet);
                    break;
            }

            if (!completed)
            {
                if (isNonNull)
                {
                    return false;
                }

                targetElement.SetNullValue();
                goto TryCompleteList_MoveNext;
            }

TryCompleteList_MoveNext:
            i++;
        }

        return true;
    }

    /// <summary>
    /// Reports that a shared list slot cannot be merged by position because the
    /// sibling results have different lengths.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the execution can continue.
    /// <c>false</c>, if the null must propagate to the parent.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ReportSharedListLengthMismatch(CompositeResultElement target)
    {
        // Non-keyed sibling lists can only be merged by position, which
        // requires an identical length. A differing shape cannot be
        // correlated, so surface an execution error and let the configured
        // null handling apply instead of silently misaligning elements.
        var error = ErrorBuilder.New()
            .SetMessage("Cannot merge shared list results with different lengths.")
            .SetPath(target.CompactPath.ToPath(target.Operation))
            .Build();
        error = _errorHandler.Handle(error);
        _store.AddError(error);

        return !_propagateNullValues;
    }

    /// <summary>
    /// Gets the error trie of the list element at <paramref name="index"/> and reports
    /// the error that is attached to the element itself.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private ErrorTrie? GetListElementErrorTrie(
        ErrorTrie errorTrie,
        CompositeResultElement target,
        int index)
    {
        errorTrie.TryGetValue(index, out var errorTrieForIndex);

        if (errorTrieForIndex?.Error is { } error)
        {
            var errorWithPath = ErrorBuilder.FromError(error)
                .SetPath(target.CompactPath.ToPath(target.Operation, index))
                .Build();
            errorWithPath = _errorHandler.Handle(errorWithPath);

            _store.AddError(errorWithPath);
        }

        return errorTrieForIndex;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ForwardListElementErrors(
        ErrorTrie errorTrieForIndex,
        CompositeResultElement target,
        int index,
        IType elementType,
        Selection selection)
        => ForwardErrors(
            errorTrieForIndex,
            target.CompactPath.ToPath(target.Operation, index),
            elementType,
            selection);

    /// <summary>
    /// Forwards every error below a null value at <paramref name="path"/>. Each error keeps
    /// its path in client response names down to the deepest segment that maps to a selection.
    /// </summary>
    private void ForwardErrors(
        ErrorTrie errorTrie,
        Path path,
        IType? type,
        Selection selection)
    {
        Stack<ErrorTrieFrame>? stack = null;

        // The direct children are walked without a stack, which is only needed for deeper errors.
        foreach (var (segment, childErrorTrie) in errorTrie)
        {
            var (childPath, childType, childSelection) =
                MapErrorPathSegment(segment, path, type, selection);

            ForwardError(childErrorTrie, childPath);

            if (childErrorTrie.Count > 0)
            {
                stack ??= new Stack<ErrorTrieFrame>();
                stack.Push(new ErrorTrieFrame(childErrorTrie, childPath, childType, childSelection));
                ForwardErrors(stack);
            }
        }
    }

    private void ForwardErrors(Stack<ErrorTrieFrame> stack)
    {
        while (stack.TryPop(out var frame))
        {
            if (!frame.Children.MoveNext())
            {
                continue;
            }

            // The frame goes back with its advanced enumerator so the siblings follow the subtree.
            stack.Push(frame);

            var (segment, childErrorTrie) = frame.Children.Current;
            var (childPath, childType, childSelection) =
                MapErrorPathSegment(segment, frame.Path, frame.Type, frame.Selection);

            ForwardError(childErrorTrie, childPath);

            if (childErrorTrie.Count > 0)
            {
                stack.Push(new ErrorTrieFrame(childErrorTrie, childPath, childType, childSelection));
            }
        }
    }

    private void ForwardError(ErrorTrie errorTrie, Path path)
    {
        if (errorTrie.Error is { } error)
        {
            var errorWithPath = ErrorBuilder.FromError(error)
                .SetPath(path)
                .Build();
            errorWithPath = _errorHandler.Handle(errorWithPath);

            _store.AddError(errorWithPath);
        }
    }

    /// <summary>
    /// Maps an error path segment below <paramref name="path"/> to the client path. A segment that
    /// does not map to a client selection keeps <paramref name="path"/> and yields no type.
    /// </summary>
    private static (Path Path, IType? Type, Selection Selection) MapErrorPathSegment(
        object segment,
        Path path,
        IType? type,
        Selection selection)
    {
        if (type?.NullableType() is { } nullableType)
        {
            if (segment is int index && nullableType.Kind is TypeKind.List)
            {
                return (path.Append(index), nullableType.ElementType(), selection);
            }

            if (segment is string responseName
                && nullableType is IObjectTypeDefinition objectType
                && selection.GetSelectionSet(objectType) is { } selectionSet
                && selectionSet.TryGetSelection(responseName, out var fieldSelection)
                && !fieldSelection.IsInternal)
            {
                return (path.Append(fieldSelection.ResponseName), fieldSelection.Type, fieldSelection);
            }
        }

        return (path, null, selection);
    }

    private static void CompleteEnumValue(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        Selection selection)
    {
        // Reached from dispatch arms guarded only by the type-system enum kind, which
        // does not guarantee the concrete Fusion CLR type, so the pattern match stays
        // as a defensive check that masks any other definition to null. A string that
        // is an accessible member of the composite enum is written through; anything
        // else (a value unknown to or inaccessible from the composite schema, or a
        // non-string kind) is masked to null so it can never leak past the gateway.
        // The raw UTF-8 payload may contain JSON escape sequences, but GraphQL enum
        // names are [A-Za-z0-9_] only, so an escaped payload cannot match any name
        // and correctly falls through to masking.
        if (selection.NamedType is FusionEnumTypeDefinition enumType
            && source.TokenType is JsonTokenType.String
            && enumType.Values.ContainsName(source.ValueSpan))
        {
            target.SetLeafValue(source);
        }
        else
        {
            target.SetNullValue();
        }
    }

    private static void CompleteEnumValue(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        FusionEnumTypeDefinition enumType)
    {
        // Reached only from call sites guarded by Selection.IsEnumValue, which
        // guarantees that the selection's named type is a FusionEnumTypeDefinition.
        // The masking semantics match the selection-based overload above.
        if (source.TokenType is JsonTokenType.String
            && enumType.Values.ContainsName(source.ValueSpan))
        {
            target.SetLeafValue(source);
        }
        else
        {
            target.SetNullValue();
        }
    }

    private bool TryCompleteObjectValue(
        Selection parentSelection,
        SourceResultElementSnapshot source,
        ErrorTrie? errorTrie,
        int depth,
        CompositeResultElement target,
        ResultSelectionSet? resultSelectionSet)
    {
        var namedType = parentSelection.NamedType;
        var objectType = Unsafe.As<ITypeDefinition, IObjectTypeDefinition>(ref namedType);

        return TryCompleteObjectValue(
            source,
            target,
            errorTrie,
            parentSelection,
            objectType,
            depth,
            resultSelectionSet,
            preparedSelectionSet: null);
    }

    private bool TryCompleteObjectValue(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        Selection parentSelection,
        IComplexTypeDefinition objectType,
        int depth,
        ResultSelectionSet? resultSelectionSet,
        SelectionSet? preparedSelectionSet)
    {
        AssertDepthAllowed(ref depth);

        if (target.IsNullOrInvalidated)
        {
            return true;
        }

        // if the property value is yet undefined we need to initialize it
        // with the current selection set.
        if (!target.TryGetObjectContext(out var objectContext))
        {
            var objectSelectionSet = preparedSelectionSet
                ?? parentSelection.GetSelectionSet(objectType)
                ?? throw new InvalidOperationException(
                    "Cannot initialize a result object without a selection set.");
            target.InitializeObjectValue(objectSelectionSet, out objectContext);
        }

        if (errorTrie is { Count: > 0 })
        {
            AddUnmappedSubFieldErrors(objectContext.SelectionSet, resultSelectionSet, errorTrie);
        }

        if (resultSelectionSet is { HasSourceResponseNameMappings: true })
        {
            return TryCompleteMappedObjectProperties(
                source,
                errorTrie,
                objectType,
                depth,
                resultSelectionSet,
                objectContext);
        }

        var properties = source.EnumeratePropertySnapshots();

        while (properties.MoveNext())
        {
            if (!objectContext.TryGetProperty(properties.Name, out var targetProperty, out var selection))
            {
                continue;
            }

            var propertyValueSnapshot = properties.Value;
            var propertyValueKind = propertyValueSnapshot.ValueKind;

            if (errorTrie is null)
            {
                // Fast path: when there are no errors and the source value is a
                // scalar (string, number, bool) we can set it directly without
                // going through the full TryCompleteValue type-dispatch chain.
                if (propertyValueKind.IsScalarValue())
                {
                    if (propertyValueKind is JsonValueKind.String && selection.IsEnumValue)
                    {
                        CompleteEnumValue(
                            propertyValueSnapshot,
                            targetProperty,
                            Unsafe.As<FusionEnumTypeDefinition>(selection.NamedType));
                        continue;
                    }

                    targetProperty.SetLeafValue(propertyValueSnapshot);
                    continue;
                }

                if (propertyValueKind is JsonValueKind.Object
                    && selection.UnwrappedKind is TypeKind.Object)
                {
                    var namedType = selection.NamedType;

                    if (!TryCompleteObjectValue(
                        propertyValueSnapshot,
                        targetProperty,
                        errorTrie: null,
                        selection,
                        Unsafe.As<ITypeDefinition, IObjectTypeDefinition>(ref namedType),
                        depth,
                        resultSelectionSet?.TryGetChild(selection.ResponseName, objectType),
                        preparedSelectionSet: null))
                    {
                        return false;
                    }

                    continue;
                }

                if (propertyValueKind is JsonValueKind.Array
                    && selection.UnwrappedKind is TypeKind.List)
                {
                    if (!TryCompleteList(
                        propertyValueSnapshot,
                        targetProperty,
                        errorTrie: null,
                        selection,
                        selection.ListElementType!,
                        selection.ListElementKind,
                        selection.IsNonNullListElement,
                        depth,
                        resultSelectionSet?.TryGetChild(selection.ResponseName, objectType)))
                    {
                        return false;
                    }

                    continue;
                }
            }

            ErrorTrie? errorTrieForResponseName = null;
            errorTrie?.TryGetValue(selection.ResponseName, out errorTrieForResponseName);

            var childSet = resultSelectionSet?.TryGetChild(selection.ResponseName, objectType);
            if (!TryCompleteValue(
                    propertyValueSnapshot,
                    targetProperty,
                    errorTrieForResponseName,
                    selection,
                    depth,
                    childSet))
            {
                return false;
            }
        }

        return true;
    }

    // Adds the errors that the source raised below an object it returned, at paths that are neither client-visible,
    // internal nor excluded at runtime, once without a path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddUnmappedSubFieldErrors(
        SelectionSet? selectionSet,
        ResultSelectionSet? fetched,
        ErrorTrie errorTrie)
    {
        foreach (var (key, childErrorTrie) in errorTrie)
        {
            if (childErrorTrie is null
                || key is not string sourceResponseName
                || (fetched is null
                    ? IsSubFieldOrExcluded(selectionSet, sourceResponseName)
                    : TryGetResponseName(fetched, sourceResponseName, out var responseName)
                        && IsSubFieldOrExcluded(selectionSet, responseName)))
            {
                continue;
            }

            AddErrorsWithoutPath(childErrorTrie);
        }

        bool IsSubFieldOrExcluded(SelectionSet? set, string name)
            => TryGetSubField(set, name, out _)
                || (set is not null
                    && set.TryGetSelection(name, out var selection)
                    && !selection.IsIncluded(_store.IncludeFlags));
    }

    /// <summary>
    /// Completes the properties of a source object whose result selection set maps
    /// source response names to different composite response names.
    /// </summary>
    /// <returns>
    /// <c>true</c>, if the object was completed.
    /// <c>false</c>, if a non-null violation must propagate to the parent.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryCompleteMappedObjectProperties(
        SourceResultElementSnapshot source,
        ErrorTrie? errorTrie,
        IComplexTypeDefinition objectType,
        int depth,
        ResultSelectionSet resultSelectionSet,
        CompositeObjectContext objectContext)
    {
        foreach (var property in source.EnumerateObject())
        {
            CompositeResultElement targetProperty;
            Selection selection;
            string sourceResponseName;

            if (resultSelectionSet.TryMapSourceResponseName(
                property,
                out var responseNameMapping))
            {
                if (!objectContext.TryGetProperty(
                    responseNameMapping.ResponseNameUtf8,
                    out var mappedTargetProperty,
                    out var mappedSelection))
                {
                    continue;
                }

                targetProperty = mappedTargetProperty;
                selection = mappedSelection;
                sourceResponseName = responseNameMapping.SourceResponseName;
            }
            else
            {
                if (!objectContext.TryGetProperty(
                    property.NameSpan,
                    out targetProperty,
                    out selection))
                {
                    continue;
                }

                sourceResponseName = selection.ResponseName;
            }

            var propertyValueSnapshot = property.Value.CreateSnapshot();
            var propertyValueKind = propertyValueSnapshot.ValueKind;

            if (errorTrie is null && propertyValueKind.IsScalarValue())
            {
                if (propertyValueKind is JsonValueKind.String && selection.IsEnumValue)
                {
                    CompleteEnumValue(
                        propertyValueSnapshot,
                        targetProperty,
                        Unsafe.As<FusionEnumTypeDefinition>(selection.NamedType));
                    continue;
                }

                targetProperty.SetLeafValue(propertyValueSnapshot);
                continue;
            }

            ErrorTrie? errorTrieForResponseName = null;
            errorTrie?.TryGetValue(sourceResponseName, out errorTrieForResponseName);

            var childSet = resultSelectionSet.TryGetChild(selection.ResponseName, objectType);
            if (!TryCompleteValue(
                    propertyValueSnapshot,
                    targetProperty,
                    errorTrieForResponseName,
                    selection,
                    depth,
                    childSet))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryCompleteAbstractValue(
        SourceResultElementSnapshot source,
        CompositeResultElement target,
        ErrorTrie? errorTrie,
        Selection selection,
        int depth,
        ResultSelectionSet? resultSelectionSet)
    {
        var isOpaque = resultSelectionSet?.ProducesOpaqueElements ?? false;
        var objectType = GetType(selection.NamedType, source.Element, isOpaque);

        if (!selection.IsInternal
            && objectType is IInaccessibleProvider { IsInaccessible: true })
        {
            _store.Result.RequireNullMarkerFinalization();
        }

        return TryCompleteObjectValue(
            source,
            target,
            errorTrie,
            selection,
            objectType,
            depth,
            resultSelectionSet,
            preparedSelectionSet: null);
    }

    /// <summary>
    /// Resolves the runtime object type for a value whose type is not selection
    /// derived, walking the wrappers of <paramref name="type"/> dynamically.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IComplexTypeDefinition GetType(IType type, SourceResultElement data, bool isOpaque)
        => GetType(type.NamedType(), data, isOpaque);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IComplexTypeDefinition GetType(ITypeDefinition namedType, SourceResultElement data, bool isOpaque)
    {
        if (namedType is IObjectTypeDefinition objectType)
        {
            return objectType;
        }

        // An opaque @interfaceObject value carries no authoritative __typename in the stand-in's
        // result, so it completes interface-typed against the interface's declared fields and only
        // recovers its concrete identity through the covering lookup.
        if (isOpaque)
        {
            return (IComplexTypeDefinition)namedType;
        }

        var typeNameElement = data.GetProperty(IntrospectionFieldNames.TypeNameSpan);

        // Small implementer sets resolve the type by comparing the raw UTF-8 __typename
        // bytes directly. Beyond 4 candidates the linear scan loses to the dictionary
        // lookup, so larger sets use the memo below.
        if (TryResolveType(typeNameElement, namedType, out var resolvedType))
        {
            return resolvedType;
        }

        // The memo only fronts the large-set fallback below; small implementer sets are
        // fully handled by TryResolveType above.
        var rawTypeName = typeNameElement.AssertUtf8String();

        if (_typeMemoA is { } typeA && Ascii.Equals(rawTypeName, typeA.Name))
        {
            return typeA;
        }

        // Two-slot MRU: a slot B hit swaps the slots so the type just seen is
        // checked first for the next element.
        if (_typeMemoB is { } typeB && Ascii.Equals(rawTypeName, typeB.Name))
        {
            _typeMemoB = _typeMemoA;
            _typeMemoA = typeB;
            return typeB;
        }

        var missedType = _schema.Types.GetType<IObjectTypeDefinition>(rawTypeName);
        _typeMemoB = _typeMemoA;
        _typeMemoA = missedType;
        return missedType;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryResolveType(
        SourceResultElement typeNameElement,
        ITypeDefinition abstractType,
        [NotNullWhen(true)] out FusionObjectTypeDefinition? objectType)
    {
        // Fusion type names are validated GraphQL names, so ASCII equality is equivalent to
        // comparing their UTF-8 encoding with ordinal semantics.
        if (abstractType is FusionUnionTypeDefinition unionType)
        {
            var possibleTypes = unionType.Types;

            if (possibleTypes.Count is > 0
                and <= FusionInterfaceTypeDefinition.MaxTypeNameLookupTypes)
            {
                return TryResolveUnionType(typeNameElement, possibleTypes, out objectType);
            }
        }
        else if (abstractType is FusionInterfaceTypeDefinition interfaceType)
        {
            var possibleTypes = interfaceType.TypeNameLookupTypes;

            if (!possibleTypes.IsDefaultOrEmpty)
            {
                return TryResolveInterfaceType(typeNameElement, possibleTypes, out objectType);
            }
        }

        objectType = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryResolveUnionType(
        SourceResultElement typeNameElement,
        FusionObjectTypeDefinitionCollection possibleTypes,
        [NotNullWhen(true)] out FusionObjectTypeDefinition? objectType)
    {
        var typeName = typeNameElement.AssertUtf8String();

        for (var i = 0; i < possibleTypes.Count; i++)
        {
            var possibleType = possibleTypes[i];

            if (Ascii.Equals(typeName, possibleType.Name))
            {
                objectType = possibleType;
                return true;
            }
        }

        objectType = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryResolveInterfaceType(
        SourceResultElement typeNameElement,
        ImmutableArray<FusionObjectTypeDefinition> possibleTypes,
        [NotNullWhen(true)] out FusionObjectTypeDefinition? objectType)
    {
        var typeName = typeNameElement.AssertUtf8String();

        for (var i = 0; i < possibleTypes.Length; i++)
        {
            var possibleType = possibleTypes[i];

            if (Ascii.Equals(typeName, possibleType.Name))
            {
                objectType = possibleType;
                return true;
            }
        }

        objectType = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssertDepthAllowed(ref int depth)
    {
        depth++;

        if (depth > _maxDepth)
        {
            throw ThrowHelper.ValueCompletionDepthNotAllowed(depth);
        }
    }

    private struct ErrorTrieFrame(
        ErrorTrie errorTrie,
        Path path,
        IType? type,
        Selection selection)
    {
        public Dictionary<object, ErrorTrie>.Enumerator Children = errorTrie.GetEnumerator();

        public readonly Path Path = path;

        public readonly IType? Type = type;

        public readonly Selection Selection = selection;
    }
}

file static class ValueCompletionExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsScalarValue(this JsonValueKind valueKind)
        => valueKind is JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IType ElementType(this IType type)
        => type switch
        {
            ListType listType => listType.ElementType,
            NonNullType { NullableType: ListType listType } => listType.ElementType,
            _ => throw new ArgumentException($"The type '{type}' is not a list type.", nameof(type))
        };
}
