using HotChocolate.Utilities;

namespace HotChocolate.Types.Composite;

/// <summary>
/// The @policy directive declares the policy groups of which at least one must be fully
/// satisfied to access the annotated member.
/// </summary>
[DirectiveType(
    DirectiveNames.Policy.Name,
    DirectiveLocation.FieldDefinition
    | DirectiveLocation.Object
    | DirectiveLocation.Interface
    | DirectiveLocation.Enum
    | DirectiveLocation.Scalar,
    IsRepeatable = false)]
[GraphQLDescription(
    """
    The @policy directive requires the client to satisfy all policies of at least one of
    the policy groups.
    """)]
public sealed class Policy
{
    private const string Name = $"@{DirectiveNames.Policy.Name}";

    /// <summary>
    /// Initializes a new instance of <see cref="Policy"/>.
    /// </summary>
    /// <param name="policies">
    /// The policy groups. All policies of a group are required together, and any single group
    /// is sufficient. Groups are stored in canonical order.
    /// </param>
    public Policy(IReadOnlyList<IReadOnlyList<string>> policies)
    {
        Policies = Canonicalize(policies);
    }

    /// <summary>
    /// Gets the policy groups in canonical order.
    /// </summary>
    [GraphQLName(DirectiveNames.Policy.Arguments.Policies)]
    [GraphQLDescription("The policy groups of which at least one must be fully satisfied.")]
    [GraphQLType<NonNullType<ListType<NonNullType<ListType<NonNullType<StringType>>>>>>]
    public IReadOnlyList<IReadOnlyList<string>> Policies { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Name}(policies: [{string.Join(", ", Policies.Select(g => $"[{string.Join(", ", g.Select(s => $"\"{s}\""))}]"))}])";

    private static IReadOnlyList<IReadOnlyList<string>> Canonicalize(
        IReadOnlyList<IReadOnlyList<string>> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        var result = new List<IReadOnlyList<string>>();

        foreach (var group in policies)
        {
            var canonical = CreateGroup(group, nameof(policies));

            if (!result.Exists(existing => existing.SequenceEqual(canonical)))
            {
                result.Add(canonical);
            }
        }

        if (result.Count == 0)
        {
            throw ThrowHelper.AuthorizationDirective_GroupIsEmpty(
                DirectiveNames.Policy.Name,
                nameof(policies));
        }

        result.Sort(CompareGroups);
        return result;
    }

    private static IReadOnlyList<string> CreateGroup(IEnumerable<string> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values);

        var set = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var policy in values)
        {
            if (string.IsNullOrEmpty(policy))
            {
                throw ThrowHelper.AuthorizationDirective_ValueIsEmpty(
                    DirectiveNames.Policy.Name,
                    paramName);
            }

            set.Add(policy);
        }

        if (set.Count == 0)
        {
            throw ThrowHelper.AuthorizationDirective_GroupIsEmpty(
                DirectiveNames.Policy.Name,
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
