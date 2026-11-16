using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types.Metadata;

namespace HotChocolate.Fusion.Planning;

internal sealed record FieldRequirementWorkItem(
    FieldSelection Selection,
    RequirementConsumer Consumer,
    Lookup? Lookup = null)
    : WorkItem
{
    public ExecutionNodeCondition[] Conditions { get; init; } = [];

    /// <summary>
    /// Gets the ids of the delivery groups whose <c>@defer</c> fragments select the field,
    /// or an empty array when the field belongs to the initial response.
    /// </summary>
    public ImmutableArray<int> DeliveryGroups { get; init; } = [];

    public override double Cost => Lookup is null ? 1 : 2;
}
