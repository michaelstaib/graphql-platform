using HotChocolate.Internal;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using HotChocolate.Utilities;

namespace HotChocolate.Types.Composite;

/// <summary>
/// The @requiresScopes directive declares the scope groups of which at least one must be fully
/// granted to access the annotated member.
/// </summary>
[DirectiveType(
    DirectiveNames.RequiresScopes.Name,
    DirectiveLocation.FieldDefinition
    | DirectiveLocation.Object
    | DirectiveLocation.Interface
    | DirectiveLocation.Enum
    | DirectiveLocation.Scalar,
    IsRepeatable = false)]
[GraphQLDescription(
    """
    The @requiresScopes directive requires the client to be granted all scopes of at least
    one of the scope groups.
    """)]
public sealed class RequiresScopes
{
    private const string Name = $"@{DirectiveNames.RequiresScopes.Name}";

    /// <summary>
    /// Initializes a new instance of <see cref="RequiresScopes"/>.
    /// </summary>
    /// <param name="scopes">
    /// The scope groups. All scopes of a group are required together, and any single group
    /// is sufficient. Groups are stored in canonical order.
    /// </param>
    public RequiresScopes(IReadOnlyList<IReadOnlyList<string>> scopes)
    {
        Scopes = Canonicalize(scopes);
    }

    /// <summary>
    /// Gets the scope groups in canonical order.
    /// </summary>
    [GraphQLName(DirectiveNames.RequiresScopes.Arguments.Scopes)]
    [GraphQLDescription("The scope groups of which at least one must be fully granted.")]
    [GraphQLType<NonNullType<ListType<NonNullType<ListType<NonNullType<StringType>>>>>>]
    public IReadOnlyList<IReadOnlyList<string>> Scopes { get; }

    internal static void AddGroup(
        IDirectiveConfigurationProvider configuration,
        ITypeInspector typeInspector,
        string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var group = CreateGroup(scopes, nameof(scopes));
        var groups = new List<IReadOnlyList<string>>();

        for (var i = configuration.Directives.Count - 1; i >= 0; i--)
        {
            if (configuration.Directives[i].Value is RequiresScopes existing)
            {
                groups.AddRange(existing.Scopes);
                configuration.Directives.RemoveAt(i);
            }
        }

        groups.Add(group);
        configuration.AddDirective(new RequiresScopes(groups), typeInspector);
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{Name}(scopes: [{string.Join(", ", Scopes.Select(g => $"[{string.Join(", ", g.Select(s => $"\"{s}\""))}]"))}])";

    private static IReadOnlyList<IReadOnlyList<string>> Canonicalize(
        IReadOnlyList<IReadOnlyList<string>> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var result = new List<IReadOnlyList<string>>();

        foreach (var group in scopes)
        {
            var canonical = CreateGroup(group, nameof(scopes));

            if (!result.Exists(existing => existing.SequenceEqual(canonical)))
            {
                result.Add(canonical);
            }
        }

        if (result.Count == 0)
        {
            throw ThrowHelper.AuthorizationDirective_GroupIsEmpty(
                DirectiveNames.RequiresScopes.Name,
                nameof(scopes));
        }

        result.Sort(CompareGroups);
        return result;
    }

    private static IReadOnlyList<string> CreateGroup(IEnumerable<string> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values);

        var set = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var scope in values)
        {
            if (string.IsNullOrEmpty(scope))
            {
                throw ThrowHelper.AuthorizationDirective_ValueIsEmpty(
                    DirectiveNames.RequiresScopes.Name,
                    paramName);
            }

            set.Add(scope);
        }

        if (set.Count == 0)
        {
            throw ThrowHelper.AuthorizationDirective_GroupIsEmpty(
                DirectiveNames.RequiresScopes.Name,
                paramName);
        }

        return [.. set];
    }

    private static int CompareGroups(IReadOnlyList<string> x, IReadOnlyList<string> y)
    {
        var length = Math.Min(x.Count, y.Count);

        for (var i = 0; i < length; i++)
        {
            var result = string.CompareOrdinal(x[i], y[i]);

            if (result != 0)
            {
                return result;
            }
        }

        return x.Count.CompareTo(y.Count);
    }
}
