using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// The sequencing state of an execution group for one result of the group root field.
/// </summary>
internal sealed class ExecutionGroupInstance
{
    internal const string ScopedContextKey = "HotChocolate.Execution.ExecutionGroupInstance";

#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly bool[] _released;
    private TaskCompletionSource?[]? _waiters;
    private int _next;

    private ExecutionGroupInstance(ExecutionGroupPlan plan, bool[] released)
    {
        Plan = plan;
        _released = released;
        AdvanceUnsafe();
    }

    /// <summary>
    /// Gets the plan that this instance sequences.
    /// </summary>
    public ExecutionGroupPlan Plan { get; }

    /// <summary>
    /// Opens an instance whose sequence contains the members that are included by the request.
    /// </summary>
    public static ExecutionGroupInstance Open(ExecutionGroupPlan plan, ConditionFlags includeFlags)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var slots = plan.Slots;
        var released = new bool[slots.Length];

        for (var i = 0; i < slots.Length; i++)
        {
            released[i] = !slots[i].Selection.IsIncluded(includeFlags);
        }

        return new ExecutionGroupInstance(plan, released);
    }

    /// <summary>
    /// Gets the instance and node that apply to a selection below the nearest group root.
    /// </summary>
    public static bool TryGetNode(
        IImmutableDictionary<string, object?> scopedContext,
        Selection selection,
        [NotNullWhen(true)] out ExecutionGroupInstance? instance,
        [NotNullWhen(true)] out ExecutionGroupNode? node)
    {
        if (scopedContext.TryGetValue(ScopedContextKey, out var value)
            && value is ExecutionGroupInstance candidate
            && candidate.Plan.TryGetNode(selection.Id, out node))
        {
            instance = candidate;
            return true;
        }

        instance = null;
        node = null;
        return false;
    }

    /// <summary>
    /// Completes when all members in front of the member of <paramref name="node"/> have finished.
    /// </summary>
    public ValueTask WaitForTurnAsync(ExecutionGroupNode node, CancellationToken cancellationToken = default)
    {
        var slot = node.SlotIndex;

        if (slot < 0)
        {
            return ValueTask.CompletedTask;
        }

        TaskCompletionSource waiter;

        lock (_sync)
        {
            if (_next >= slot || _released[slot])
            {
                return ValueTask.CompletedTask;
            }

            _waiters ??= new TaskCompletionSource?[_released.Length];
            waiter = _waiters[slot] ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return new ValueTask(waiter.Task.WaitAsync(cancellationToken));
    }

    /// <summary>
    /// Ends the turn of the member of <paramref name="node"/>. When <paramref name="releaseDescendants"/>
    /// is <c>true</c> the members below the selection are released as well because they will not run.
    /// </summary>
    public void Complete(ExecutionGroupNode node, bool releaseDescendants)
    {
        lock (_sync)
        {
            if (node.SlotIndex >= 0)
            {
                _released[node.SlotIndex] = true;
            }

            if (releaseDescendants)
            {
                foreach (var slot in node.DescendantSlotIndexes)
                {
                    _released[slot] = true;
                }
            }

            AdvanceUnsafe();
        }
    }

    /// <summary>
    /// Releases all remaining members.
    /// </summary>
    public void Abandon()
    {
        lock (_sync)
        {
            Array.Fill(_released, true);
            AdvanceUnsafe();
        }
    }

    private void AdvanceUnsafe()
    {
        var previous = _next;

        while (_next < _released.Length && _released[_next])
        {
            _next++;
        }

        if (_waiters is null)
        {
            return;
        }

        // the waiters are completed asynchronously, so signaling under the lock is safe.
        var last = Math.Min(_next, _released.Length - 1);

        for (var i = previous; i <= last; i++)
        {
            _waiters[i]?.TrySetResult();
        }
    }
}
