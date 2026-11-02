namespace HotChocolate.Fusion.Planning;

/// <summary>
/// A segment of a field path, identifying a field by its name and optional alias.
/// <see cref="TypeCondition"/> carries the name of the type condition that was
/// active when a composite field was entered, so the incremental plan operation
/// can re-wrap that field in an <c>... on Type</c> inline fragment when its
/// enclosing field returns an abstract type. <see cref="Conditions"/> carries the
/// <c>@skip</c> and <c>@include</c> conditions of the inline fragments the field was selected under.
/// </summary>
internal readonly record struct FieldPathSegment(
    string FieldName,
    string? Alias,
    string? TypeCondition,
    FragmentConditions Conditions)
{
    public string ResponseName => Alias ?? FieldName;
}
