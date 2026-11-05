using System.Collections.Immutable;
using HotChocolate.Execution;

namespace HotChocolate.Fusion.Policies;

/// <summary>
/// The request feature that carries the <see cref="PolicyOutcome"/> of every variable set.
/// </summary>
internal sealed class PolicyOutcomeFeature : RequestFeature
{
    /// <summary>
    /// Gets or sets the outcomes by variable set index. An entry is <c>null</c> if every
    /// selection of the variable set is allowed.
    /// </summary>
    public ImmutableArray<PolicyOutcome?> Outcomes { get; set; }

    protected internal override void Reset()
    {
        Outcomes = default;
    }
}
