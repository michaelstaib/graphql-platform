using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// A conditional <c>@defer(if: $variable)</c> fragment that starts its own delivery group inside an
/// enclosing delivery group, together with the position that fragment occupies in the operation.
/// </summary>
/// <param name="Fragment">The conditional <c>@defer</c> fragment.</param>
/// <param name="EnclosingDeliveryGroup">The delivery group whose selection contains the fragment.</param>
/// <param name="ParentPath">The field path of the selection set that contains the fragment.</param>
/// <param name="ParentTypeCondition">
/// The type condition active at the fragment, or <c>null</c> when none applies.
/// </param>
internal sealed record ConditionalFragmentOccurrence(
    InlineFragmentNode Fragment,
    DeliveryGroup EnclosingDeliveryGroup,
    ImmutableArray<FieldPathSegment> ParentPath,
    NamedTypeNode? ParentTypeCondition);
