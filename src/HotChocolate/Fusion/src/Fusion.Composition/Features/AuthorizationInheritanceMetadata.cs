using System.Collections.Immutable;

namespace HotChocolate.Fusion.Features;

/// <summary>
/// The members of the merged schema that newly require authorization because of interface
/// inheritance, although none of their source schemas annotated them directly.
/// </summary>
internal sealed class AuthorizationInheritanceMetadata
{
    public List<InheritedAuthorization> Entries { get; } = [];
}

/// <summary>
/// A merged member that is protected by inheritance.
/// </summary>
/// <param name="Coordinate">The protected member.</param>
/// <param name="Paths">
/// The paths from the members that contribute the requirement to the protected member.
/// </param>
/// <param name="SourceSchemas">The source schemas that annotated the contributing members.</param>
internal sealed record InheritedAuthorization(
    SchemaCoordinate Coordinate,
    ImmutableArray<string> Paths,
    ImmutableArray<string> SourceSchemas);
