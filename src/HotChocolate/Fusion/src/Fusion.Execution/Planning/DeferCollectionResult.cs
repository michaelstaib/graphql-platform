using System.Collections.Immutable;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// The output of <see cref="DeferOccurrenceCollector"/>: the leaf field occurrences and the
/// conditional <c>@defer</c> fragments nested inside another delivery group.
/// </summary>
internal sealed record DeferCollectionResult(
    ImmutableArray<FieldOccurrence> Occurrences,
    ImmutableArray<ConditionalFragmentOccurrence> ConditionalFragments);
