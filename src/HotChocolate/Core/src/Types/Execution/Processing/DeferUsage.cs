using System.Runtime.CompilerServices;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// Represents a usage of the <c>@defer</c> directive encountered during operation compilation.
/// Forms a parent chain to model nested defer scopes.
/// </summary>
/// <param name="Label">
/// The optional label from <c>@defer(label: "...")</c>, used to identify the deferred
/// payload in the incremental delivery response.
/// </param>
/// <param name="Parent">
/// The parent defer usage when this <c>@defer</c> is nested inside another deferred fragment,
/// or <c>null</c> if this is a top-level defer.
/// </param>
/// <param name="DeferConditionIndex">
/// The index into the <see cref="DeferConditionCollection"/> for the <c>if</c> condition
/// associated with this defer directive. This index maps to a bit position in the
/// runtime defer flags bitmask.
/// </param>
public sealed record DeferUsage(
    string? Label,
    DeferUsage? Parent,
    int DeferConditionIndex)
{
    /// <summary>
    /// Gets the creation order of this defer usage within its operation.
    /// Each defer usage of an operation is one instance and therefore has one ordinal.
    /// </summary>
    public int Ordinal { get; internal init; }

    /// <summary>
    /// Determines whether <paramref name="other"/> is this instance. A defer usage is equal only to itself.
    /// </summary>
    public bool Equals(DeferUsage? other)
        => ReferenceEquals(this, other);

    /// <inheritdoc />
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);

    /// <summary>
    /// Gets the nearest defer usage in the parent chain of <paramref name="usage"/>, starting with
    /// <paramref name="usage"/> itself, whose condition is enabled, or <c>null</c> if none is enabled.
    /// </summary>
    internal static DeferUsage? FindActive(DeferUsage? usage, ConditionFlags deferFlags)
    {
        while (usage is not null && !deferFlags.IsSet(usage.DeferConditionIndex))
        {
            usage = usage.Parent;
        }

        return usage;
    }
}
