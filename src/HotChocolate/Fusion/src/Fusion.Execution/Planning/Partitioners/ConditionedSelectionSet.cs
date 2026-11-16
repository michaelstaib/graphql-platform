using HotChocolate.Fusion.Execution.Nodes;

namespace HotChocolate.Fusion.Planning.Partitioners;

/// <summary>
/// A selection set a source schema could not resolve, the conditions it is selected under and, when it is
/// the content of a <c>@defer</c> fragment, the id of the delivery group of that fragment.
/// </summary>
internal readonly record struct ConditionedSelectionSet(
    SelectionSet SelectionSet,
    ExecutionNodeCondition[] Conditions,
    int? DeliveryGroup);
