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
    {
        if (target.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

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

        return !element.IsNullOrInvalidated;
    }

    private void ClearMissingTargets()
    {
        _missingTargetCount = 0;
        _missingHopId = NoHop;
        ReturnPooled(ref _missingTargets);
    }

    /// <summary>
    /// Completes the non-null fields of the hops of <paramref name="plan"/> that are still undefined with a non-null
    /// violation, for the targets the hops could not fetch and for the targets of the hops that
    /// <paramref name="state"/> skipped because a dependency failed. The hops are completed in the order of their ids.
    /// </summary>
    /// <returns>
    /// <c>false</c>, if the null propagated to the root.
    /// </returns>
    internal bool CompleteDependentFields(IOperationPlan plan, ExecutionState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_missingTargetCount == 0 && !state.HasCompletingSkips)
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
                Array.Sort(hops, 0, hopCount);

                for (var i = 0; i < hopCount; i++)
                {
                    if (i > 0 && hops[i] == hops[i - 1])
                    {
                        continue;
                    }

                    if (!CompleteHopUnsafe(plan, state, hops[i]))
                    {
                        return false;
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
                    operation.ResultSelectionSet);

            case ApolloOperationExecutionNode { Requirements.Length: > 0 } operation when operation.Id == hopId:
                return CompleteHopUnsafe(
                    hopId,
                    state.IsCompletingSkip(plan, hopId),
                    [operation.Target],
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
                    => CompleteHopUnsafe(hopId, isSkipped, [single.Target], single.ResultSelectionSet),
                BatchOperationDefinition batch
                    => CompleteHopUnsafe(hopId, isSkipped, batch.Targets, batch.ResultSelectionSet),
                _ => true
            };
        }

        return true;
    }

    private bool CompleteHopUnsafe(
        int hopId,
        bool isSkipped,
        ReadOnlySpan<SelectionPath> targets,
        ResultSelectionSet resultSelectionSet)
    {
        for (var i = 0; i < _missingTargetCount; i++)
        {
            if (_missingTargets[i * 2] != hopId)
            {
                continue;
            }

            var target = new CompositeResultElement(
                _result,
                new CompositeResultDocument.Cursor(_missingTargets[(i * 2) + 1]));

            if (!_valueCompletion.CompleteUndefinedFields(target, resultSelectionSet))
            {
                return false;
            }
        }

        if (!isSkipped)
        {
            return true;
        }

        var count = CollectCombinedElementsUnsafe(targets, ref _collectTargetCombined);

        for (var i = 0; i < count; i++)
        {
            if (!_valueCompletion.CompleteUndefinedFields(_collectTargetCombined[i], resultSelectionSet))
            {
                return false;
            }
        }

        return true;
    }
}
