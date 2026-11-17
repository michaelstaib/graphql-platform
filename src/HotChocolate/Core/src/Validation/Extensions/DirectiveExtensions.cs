using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Validation;

internal static class DirectiveExtensions
{
    internal static bool HasSkipOrIncludeDirective(this IReadOnlyList<DirectiveNode> directives)
    {
        if (directives.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < directives.Count; i++)
        {
            var directive = directives[i];

            if (directive.Name.Value.Equals(DirectiveNames.Skip.Name, StringComparison.Ordinal)
                || directive.Name.Value.Equals(DirectiveNames.Include.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasStreamOrDeferDirective(this IReadOnlyList<DirectiveNode> directives)
    {
        if (directives.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < directives.Count; i++)
        {
            if (directives[i].IsDeferOrStreamDirective())
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsDeferOrStreamDirective(this DirectiveNode directive)
        => directive.Name.Value.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal)
            || directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal);
}
