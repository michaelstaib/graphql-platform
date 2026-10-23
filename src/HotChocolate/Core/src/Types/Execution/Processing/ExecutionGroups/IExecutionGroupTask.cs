namespace HotChocolate.Execution.Processing;

/// <summary>
/// A task that waits in an execution group scope until it is its turn.
/// </summary>
internal interface IExecutionGroupTask
{
    /// <summary>
    /// Gets the slot of this task in its execution group scope.
    /// </summary>
    int GroupSlot { get; }

    /// <summary>
    /// Registers this task with the work scheduler.
    /// </summary>
    void Register();

    /// <summary>
    /// Counts this task with the work scheduler without making it available for execution.
    /// </summary>
    void Reserve();

    /// <summary>
    /// Makes this task available for execution after it was counted with <see cref="Reserve"/>.
    /// </summary>
    void RegisterReserved();
}
