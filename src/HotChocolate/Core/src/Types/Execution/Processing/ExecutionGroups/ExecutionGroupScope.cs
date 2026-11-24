using System.Numerics;
using HotChocolate.Execution.Processing.Tasks;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// The queue of an execution group for one result of the group root field. Only the task of the member
/// whose turn it is is handed to the work scheduler.
/// </summary>
internal sealed class ExecutionGroupScope
{
    private static readonly ObjectPool<ExecutionGroupScope> s_pool =
        new DefaultObjectPool<ExecutionGroupScope>(new ScopePolicy(), 1024);

    private ulong[] _released = new ulong[1];
    private ulong[] _unsequenced = new ulong[1];
    private ResolverTask?[] _tasks = new ResolverTask?[4];
    private StreamTask?[] _streams = new StreamTask?[4];
    private WorkScheduler _scheduler = null!;
    private ExecutionGroupPlan _plan = null!;
    private bool _honorsStreams;
    private int _slotCount;
    private int _wordCount;

    /// <summary>
    /// Gets the plan that this scope sequences.
    /// </summary>
    public ExecutionGroupPlan Plan => _plan;

    /// <summary>
    /// Gets a value indicating whether the streamed members of the group stream for this result.
    /// </summary>
    public bool HonorsStreams => _honorsStreams;

    /// <summary>
    /// Gets the slot whose turn it is, or the number of slots if all members have ended their turn.
    /// </summary>
    public int Position
    {
        get
        {
            for (var i = 0; i < _wordCount; i++)
            {
                var open = ~Volatile.Read(ref _released[i]);

                if (open != 0)
                {
                    return (i << 6) + BitOperations.TrailingZeroCount(open);
                }
            }

            return _slotCount;
        }
    }

    /// <summary>
    /// Gets or sets the next scope in the list of scopes that are owned by one work scheduler.
    /// </summary>
    public ExecutionGroupScope? NextScope { get; set; }

    /// <summary>
    /// Opens a scope whose queue contains the members that are included by the request, or returns
    /// <c>null</c> if fewer than two members are included and there is nothing to sequence.
    /// </summary>
    public static ExecutionGroupScope? Open(
        WorkScheduler scheduler,
        ExecutionGroupPlan plan,
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        DeferUsage? rootDeferUsage,
        IVariableValueCollection variables)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Slots.Length < 2)
        {
            return null;
        }

        var honorsStreams = plan.HonorsStreams(includeFlags, deferFlags, rootDeferUsage, variables);
        var scope = s_pool.Get();

        if (scope.Initialize(scheduler, plan, includeFlags, deferFlags, rootDeferUsage, honorsStreams) < 2)
        {
            scope.Return();
            return null;
        }

        return scope;
    }

    /// <summary>
    /// Gets a value indicating whether the task of the specified slot may run, because it is its turn, it was
    /// released, or it is a streamed member in the payload of the group root, whose initial items are not
    /// sequenced.
    /// </summary>
    public bool CanRun(int slot) => IsReleased(slot) || Position == slot || IsUnsequenced(slot);

    /// <summary>
    /// Holds the stream of a member until its slot has its turn and then starts reading the remaining items.
    /// </summary>
    public void Enqueue(StreamTask stream, int slot)
    {
        Interlocked.Exchange(ref _streams[slot], stream);

        if (IsReleased(slot) || Position == slot)
        {
            Dispatch(slot);
        }
    }

    /// <summary>
    /// Holds the task of a member until its slot may run.
    /// </summary>
    public void Enqueue(ResolverTask task)
    {
        var slot = task.GroupSlot;
        Interlocked.Exchange(ref _tasks[slot], task);

        if (CanRun(slot))
        {
            Dispatch(slot);
        }
    }

    /// <summary>
    /// Ends the turn of a member, or skips a member that will not run, and starts the next member.
    /// </summary>
    public void Release(int slot)
    {
        ref var word = ref _released[slot >> 6];
        var bit = 1UL << (slot & 63);

        if ((Interlocked.Or(ref word, bit) & bit) == 0)
        {
            Dispatch(slot);
            Dispatch(Position);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the specified selection is a member of the group of this scope.
    /// </summary>
    public bool IsMember(Selection selection)
        => _plan.TryGetNode(selection.Id, out var node) && node.SlotIndex >= 0;

    /// <summary>
    /// Skips the members below the specified selection.
    /// </summary>
    public void ReleaseSlotsBelow(Selection selection)
    {
        if (_plan.TryGetNode(selection.Id, out var node))
        {
            foreach (var slot in node.SlotsBelow)
            {
                Release(slot);
            }
        }
    }

    /// <summary>
    /// Ends the sequencing so that all queued tasks start and later tasks are not queued.
    /// </summary>
    public void Abandon()
    {
        for (var i = 0; i < _wordCount; i++)
        {
            Interlocked.Exchange(ref _released[i], ulong.MaxValue);
        }

        for (var i = 0; i < _slotCount; i++)
        {
            Dispatch(i);
        }
    }

    /// <summary>
    /// Resets the scope and gives it back to the pool.
    /// </summary>
    public void Return()
    {
        Array.Clear(_tasks, 0, _slotCount);
        Array.Clear(_streams, 0, _slotCount);
        _scheduler = null!;
        _plan = null!;
        _honorsStreams = false;
        _slotCount = 0;
        _wordCount = 0;
        NextScope = null;
        s_pool.Return(this);
    }

    private int Initialize(
        WorkScheduler scheduler,
        ExecutionGroupPlan plan,
        ConditionFlags includeFlags,
        ConditionFlags deferFlags,
        DeferUsage? rootDeferUsage,
        bool honorsStreams)
    {
        var slots = plan.Slots;
        var slotCount = slots.Length;
        var wordCount = (slotCount + 63) >> 6;

        if (_released.Length < wordCount)
        {
            _released = new ulong[wordCount];
            _unsequenced = new ulong[wordCount];
        }

        if (_tasks.Length < slotCount)
        {
            _tasks = new ResolverTask?[slotCount];
            _streams = new StreamTask?[slotCount];
        }

        Array.Clear(_released, 0, wordCount);
        Array.Clear(_unsequenced, 0, wordCount);

        var included = 0;

        for (var i = 0; i < slotCount; i++)
        {
            var slot = slots[i];

            if (slot.Selection.IsIncluded(includeFlags))
            {
                included++;

                // a streamed member reads its initial items with the root, only its remaining items are sequenced.
                if (honorsStreams
                    && slot.IsStreamable
                    && slot.Selection.GetPrimaryDeferUsage(deferFlags) == rootDeferUsage)
                {
                    _unsequenced[i >> 6] |= 1UL << (i & 63);
                }
            }
            else
            {
                _released[i >> 6] |= 1UL << (i & 63);
            }
        }

        var tail = slotCount & 63;

        if (tail != 0)
        {
            _released[wordCount - 1] |= ulong.MaxValue << tail;
        }

        _scheduler = scheduler;
        _plan = plan;
        _honorsStreams = honorsStreams;
        _slotCount = slotCount;
        _wordCount = wordCount;
        return included;
    }

    private bool IsReleased(int slot)
        => (Volatile.Read(ref _released[slot >> 6]) & (1UL << (slot & 63))) != 0;

    private bool IsUnsequenced(int slot)
        => (_unsequenced[slot >> 6] & (1UL << (slot & 63))) != 0;

    private void Dispatch(int slot)
    {
        if (slot >= _slotCount)
        {
            return;
        }

        if (Interlocked.Exchange(ref _tasks[slot], null) is { } task)
        {
            _scheduler.RegisterReserved(task);
        }

        if (Interlocked.Exchange(ref _streams[slot], null) is { } stream)
        {
            stream.StartProducer();
        }
    }

    private sealed class ScopePolicy : PooledObjectPolicy<ExecutionGroupScope>
    {
        public override ExecutionGroupScope Create() => new();

        public override bool Return(ExecutionGroupScope obj) => true;
    }
}
