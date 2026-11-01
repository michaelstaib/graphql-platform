using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// The <c>@skip</c> and <c>@include</c> conditions of the inline fragments that enclose a
/// selection, outermost first. The conditions can be empty. Equality is sequence-based on
/// <see cref="Items"/>.
/// </summary>
internal readonly record struct FragmentConditions(ImmutableArray<IncludeCondition> Items)
    : IEquatable<FragmentConditions>
{
    public static FragmentConditions Empty { get; } = new([]);

    public bool IsEmpty => Items.IsDefaultOrEmpty;

    public FragmentConditions Add(IncludeCondition condition)
        => new(IsEmpty ? [condition] : Items.Add(condition));

    public bool Equals(FragmentConditions other)
    {
        if (IsEmpty)
        {
            return other.IsEmpty;
        }

        if (other.IsEmpty || Items.Length != other.Items.Length)
        {
            return false;
        }

        for (var i = 0; i < Items.Length; i++)
        {
            if (!Items[i].Equals(other.Items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        if (IsEmpty)
        {
            return 0;
        }

        var hash = new HashCode();

        for (var i = 0; i < Items.Length; i++)
        {
            hash.Add(Items[i]);
        }

        return hash.ToHashCode();
    }
}
