using System.Buffers;
using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Text.Json;

namespace HotChocolate.Fusion.Execution.Results;

internal sealed partial class FetchResultStore
{
    /// <summary>
    /// The hop id that variable creation passes when its targets are not the targets of a hop.
    /// </summary>
    internal const int NoHop = -1;

    // The hop id and the cursor of every target a hop that ran did not fetch and did not reuse.
    private int[] _missingTargets = [];
    private int _missingTargetCount;
    private int _missingHopId = NoHop;

    /// <summary>
    /// Gets the number of targets that hops could not fetch and that the next completion pass looks at.
    /// </summary>
    internal int MissingTargetCount => _missingTargetCount;

    private bool HasKeyFetchErrors => _keyFetchErrors is { Count: > 0 };

    internal ConditionFlags IncludeFlags => new(_includeFlags, _wideIncludeFlags);

    // Records a reachable target that the hop currently creating its variables could neither fetch nor reuse.
    private void RecordMissingTargetUnsafe(CompositeResultElement target)
    {
        if (_missingHopId < 0 || !IsReachableObjectUnsafe(target))
        {
            return;
        }

        var index = _missingTargetCount * 2;
        EnsureLength(ref _missingTargets, index + 2, index);
        _missingTargets[index] = _missingHopId;
        _missingTargets[index + 1] = target.Cursor.Value;
        _missingTargetCount++;
    }

    /// <summary>
    /// Gets whether <paramref name="target"/> is an object that neither it nor any of its ancestors up to the
    /// root is <c>null</c> or invalidated. The ancestors are read from the root down, so the target is
    /// unreachable even when the row of the target itself is intact.
    /// </summary>
    internal bool IsReachableObjectUnsafe(CompositeResultElement target)
        => target.ValueKind is JsonValueKind.Object && IsReachablePathUnsafe(target, allowNullTarget: false);

    /// <summary>
    /// Gets whether none of the ancestors of <paramref name="target"/> up to the root is <c>null</c> or
    /// invalidated. The target itself may be <c>null</c>.
    /// </summary>
    internal bool HasReachableAncestorsUnsafe(CompositeResultElement target)
        => IsReachablePathUnsafe(target, allowNullTarget: true);

    private bool IsReachablePathUnsafe(CompositeResultElement target, bool allowNullTarget)
    {
        var path = target.CompactPath;
        var element = _result.Data;

        for (var i = 0; i < path.Length; i++)
        {
            if (element.IsNullOrInvalidated)
            {
                return false;
            }

            var segment = path[i];

            if (segment >= 0)
            {
                if (element.ValueKind is not JsonValueKind.Object)
                {
                    return false;
                }

                element = element.GetPropertyBySelectionId(segment);
            }
            else
            {
                var index = ~segment;

                if (element.ValueKind is not JsonValueKind.Array || index >= element.GetArrayLength())
                {
                    return false;
                }

                element = element[index];
            }
        }

        return allowNullTarget || !element.IsNullOrInvalidated;
    }

    private void ClearMissingTargets()
    {
        _missingTargetCount = 0;
        _missingHopId = NoHop;
        ReturnPooled(ref _missingTargets);
    }

    /// <summary>
    /// Completes the dependent fields of the hops of <paramref name="plan"/> that are still undefined, for the
    /// targets the hops could not fetch and for the targets of the hops that <paramref name="state"/> skipped
    /// because a dependency failed. A target whose requirement field failed reports that error on its dependent
    /// fields, any other target completes its undefined non-null fields with a non-null violation. The hops are
    /// completed by the depth of their dependencies, then by their ids.
    /// </summary>
    /// <param name="plan">The plan whose hops are completed.</param>
    /// <param name="state">The execution state of the plan.</param>
    /// <param name="failure">
    /// The execution node of the first hop that could not be completed and the exception that was thrown for it.
    /// The remaining hops are still completed.
    /// </param>
    /// <returns>
    /// <c>false</c>, if the null propagated to the root.
    /// </returns>
    internal bool CompleteDependentFields(
        IOperationPlan plan,
        ExecutionState state,
        out (ExecutionNode Node, Exception Error)? failure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        failure = null;

        lock (_lock)
        {
            var hasKeyFetchErrors = HasKeyFetchErrors;

            if (_missingTargetCount == 0 && !state.HasCompletingSkips && !hasKeyFetchErrors)
            {
                return true;
            }

            var hops = ArrayPool<int>.Shared.Rent(Math.Max(_missingTargetCount, 8));

            try
            {
                var hopCount = 0;

                for (var i = 0; i < _missingTargetCount; i++)
                {
                    hops[hopCount++] = _missingTargets[i * 2];
                }

                hopCount = state.AppendCompletingSkippedNodes(plan, ref hops, hopCount);

                if (hasKeyFetchErrors)
                {
                    hopCount = AppendHopIds(plan, ref hops, hopCount);
                }

                SortByDependencyDepth(plan, hops.AsSpan(0, hopCount));

                for (var i = 0; i < hopCount; i++)
                {
                    if (i > 0 && hops[i] == hops[i - 1])
                    {
                        continue;
                    }

                    try
                    {
                        if (!CompleteHopUnsafe(plan, state, hops[i]))
                        {
                            return false;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failure ??= (plan.GetNodeById(hops[i]), ex);
                    }
                }

                return true;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(hops);

                // The next pass of a mutation only looks at what its root field adds.
                _missingTargetCount = 0;
                state.ClearCompletingSkips();
            }
        }
    }

    private static void SortByDependencyDepth(IOperationPlan plan, Span<int> hopIds)
    {
        var depths = new Dictionary<int, int>();
        var keys = new long[hopIds.Length];

        for (var i = 0; i < hopIds.Length; i++)
        {
            keys[i] = ((long)GetDependencyDepth(plan, hopIds[i], depths) << 32) | (uint)hopIds[i];
        }

        Array.Sort(keys);

        for (var i = 0; i < keys.Length; i++)
        {
            hopIds[i] = (int)(keys[i] & uint.MaxValue);
        }
    }

    private static int GetDependencyDepth(IOperationPlan plan, int nodeId, Dictionary<int, int> depths)
    {
        if (depths.TryGetValue(nodeId, out var depth))
        {
            return depth;
        }

        depths[nodeId] = 0;
        var node = FindPlanNode(plan, nodeId);

        foreach (var dependency in node.Dependencies)
        {
            depth = Math.Max(depth, GetDependencyDepth(plan, dependency.Id, depths) + 1);
        }

        foreach (var dependency in node.OptionalDependencies)
        {
            depth = Math.Max(depth, GetDependencyDepth(plan, dependency.Id, depths) + 1);
        }

        depths[nodeId] = depth;
        return depth;
    }

    private static IOperationPlanNode FindPlanNode(IOperationPlan plan, int nodeId)
    {
        var node = plan.GetNodeById(nodeId);

        if (node.Id == nodeId)
        {
            return node;
        }

        // The id belongs to an operation definition inside a batch.
        var definition = node switch
        {
            OperationBatchExecutionNode batch => FindDefinition(batch.Operations, nodeId),
            ApolloOperationBatchExecutionNode batch => FindDefinition(batch.Operations, nodeId),
            _ => null
        };

        return (IOperationPlanNode?)definition ?? node;
    }

    private static OperationDefinition? FindDefinition(ReadOnlySpan<OperationDefinition> operations, int nodeId)
    {
        foreach (var operation in operations)
        {
            if (operation.Id == nodeId)
            {
                return operation;
            }
        }

        return null;
    }

    private static int AppendHopIds(IOperationPlan plan, ref int[] hops, int count)
    {
        foreach (var node in plan.AllNodes)
        {
            switch (node)
            {
                case OperationExecutionNode { Requirements.Length: > 0 }:
                case ApolloOperationExecutionNode { Requirements.Length: > 0 }:
                    count = AppendHopId(ref hops, count, node.Id);
                    break;

                case OperationBatchExecutionNode batch:
                    foreach (var operation in batch.Operations)
                    {
                        if (operation.Requirements.Length > 0)
                        {
                            count = AppendHopId(ref hops, count, operation.Id);
                        }
                    }

                    break;

                case ApolloOperationBatchExecutionNode batch:
                    foreach (var operation in batch.Operations)
                    {
                        if (operation.Requirements.Length > 0)
                        {
                            count = AppendHopId(ref hops, count, operation.Id);
                        }
                    }

                    break;
            }
        }

        return count;
    }

    private static int AppendHopId(ref int[] hops, int count, int hopId)
    {
        if (count == hops.Length)
        {
            var grown = ArrayPool<int>.Shared.Rent(count * 2);
            hops.AsSpan(0, count).CopyTo(grown);
            ArrayPool<int>.Shared.Return(hops);
            hops = grown;
        }

        hops[count++] = hopId;
        return count;
    }

    // Resolves the hop with the id and completes the targets that were recorded for it and, when the hop was skipped,
    // all of its targets. Only operations with requirements are hops, other nodes and the nodes that own a batch of
    // operations are not.
    private bool CompleteHopUnsafe(IOperationPlan plan, ExecutionState state, int hopId)
    {
        var node = plan.GetNodeById(hopId);

        switch (node)
        {
            case OperationExecutionNode { Requirements.Length: > 0 } operation when operation.Id == hopId:
                return CompleteHopUnsafe(
                    hopId,
                    state.IsCompletingSkip(plan, hopId),
                    [operation.Target],
                    operation.Requirements,
                    operation.ResultSelectionSet);

            case ApolloOperationExecutionNode { Requirements.Length: > 0 } operation when operation.Id == hopId:
                return CompleteHopUnsafe(
                    hopId,
                    state.IsCompletingSkip(plan, hopId),
                    [operation.Target],
                    operation.Requirements,
                    operation.ResultSelectionSet);

            case OperationBatchExecutionNode batch:
                return CompleteDefinitionUnsafe(plan, state, batch.Operations, hopId);

            case ApolloOperationBatchExecutionNode batch:
                return CompleteDefinitionUnsafe(plan, state, batch.Operations, hopId);

            default:
                return true;
        }
    }

    private bool CompleteDefinitionUnsafe(
        IOperationPlan plan,
        ExecutionState state,
        ReadOnlySpan<OperationDefinition> operations,
        int hopId)
    {
        foreach (var operation in operations)
        {
            if (operation.Id != hopId || operation.Requirements.Length == 0)
            {
                continue;
            }

            var isSkipped = state.IsCompletingSkip(plan, hopId);

            return operation switch
            {
                SingleOperationDefinition single
                    => CompleteHopUnsafe(
                        hopId,
                        isSkipped,
                        [single.Target],
                        single.Requirements,
                        single.ResultSelectionSet),
                BatchOperationDefinition batch
                    => CompleteHopUnsafe(
                        hopId,
                        isSkipped,
                        batch.Targets,
                        batch.Requirements,
                        batch.ResultSelectionSet),
                _ => true
            };
        }

        return true;
    }

    private bool CompleteHopUnsafe(
        int hopId,
        bool isSkipped,
        ReadOnlySpan<SelectionPath> targets,
        ReadOnlySpan<OperationRequirement> requirements,
        ResultSelectionSet resultSelectionSet)
    {
        var requirementFieldNames = HasKeyFetchErrors ? GetRequirementFieldNames(targets, requirements) : null;

        for (var i = 0; i < _missingTargetCount; i++)
        {
            if (_missingTargets[i * 2] != hopId)
            {
                continue;
            }

            var target = new CompositeResultElement(
                _result,
                new CompositeResultDocument.Cursor(_missingTargets[(i * 2) + 1]));

            if (!CompleteTargetUnsafe(target, requirementFieldNames, resultSelectionSet))
            {
                return false;
            }
        }

        if (isSkipped)
        {
            var count = CollectCombinedElementsUnsafe(targets, ref _collectTargetCombined);

            for (var i = 0; i < count; i++)
            {
                if (!CompleteTargetUnsafe(_collectTargetCombined[i], requirementFieldNames, resultSelectionSet))
                {
                    return false;
                }
            }
        }

        return requirementFieldNames is null
            || ReportKeyFetchErrorsOfNulledEntitiesUnsafe(targets, requirementFieldNames, resultSelectionSet);
    }

    private bool CompleteTargetUnsafe(
        CompositeResultElement target,
        List<string>? requirementFieldNames,
        ResultSelectionSet resultSelectionSet)
    {
        return requirementFieldNames is not null
            && TryGetKeyFetchErrorUnsafe(target.Path, requirementFieldNames, out var originName, out var error)
                ? ReportKeyFetchErrorUnsafe(target, resultSelectionSet, originName, error)
                : _valueCompletion.CompleteUndefinedFields(target, resultSelectionSet);
    }
}
