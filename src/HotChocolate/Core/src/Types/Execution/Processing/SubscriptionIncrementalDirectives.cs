using System.Diagnostics.CodeAnalysis;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Execution.Processing;

internal static class SubscriptionIncrementalDirectives
{
    /// <summary>
    /// Finds a @defer or @stream directive of the operation that is enabled for the
    /// specified variable values and is not excluded by @skip or @include.
    /// </summary>
    public static bool TryFindEnabled(
        OperationDefinitionNode operation,
        IVariableValueCollection variables,
        [NotNullWhen(true)] out DirectiveNode? directive)
        => TryFindEnabled(operation.SelectionSet, variables, out directive);

    private static bool TryFindEnabled(
        SelectionSetNode selectionSet,
        IVariableValueCollection variables,
        [NotNullWhen(true)] out DirectiveNode? directive)
    {
        for (var i = 0; i < selectionSet.Selections.Count; i++)
        {
            var selection = selectionSet.Selections[i];

            if (!IsIncluded(selection.Directives, variables))
            {
                continue;
            }

            for (var j = 0; j < selection.Directives.Count; j++)
            {
                var candidate = selection.Directives[j];

                if (IsEnabled(candidate, variables))
                {
                    directive = candidate;
                    return true;
                }
            }

            var nested = selection switch
            {
                FieldNode field => field.SelectionSet,
                InlineFragmentNode inlineFragment => inlineFragment.SelectionSet,
                _ => null
            };

            if (nested is not null && TryFindEnabled(nested, variables, out directive))
            {
                return true;
            }
        }

        directive = null;
        return false;
    }

    private static bool IsIncluded(
        IReadOnlyList<DirectiveNode> directives,
        IVariableValueCollection variables)
    {
        for (var i = 0; i < directives.Count; i++)
        {
            var directive = directives[i];
            var name = directive.Name.Value;

            if (name.Equals(DirectiveNames.Skip.Name, StringComparison.Ordinal)
                && DirectiveIfCondition.IsEnabled(
                    name,
                    directive.GetArgumentValue(DirectiveNames.Skip.Arguments.If),
                    variables))
            {
                return false;
            }

            if (name.Equals(DirectiveNames.Include.Name, StringComparison.Ordinal)
                && !DirectiveIfCondition.IsEnabled(
                    name,
                    directive.GetArgumentValue(DirectiveNames.Include.Arguments.If),
                    variables))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEnabled(DirectiveNode directive, IVariableValueCollection variables)
    {
        var name = directive.Name.Value;

        return (name.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal)
                || name.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal))
            && DirectiveIfCondition.IsEnabled(
                name,
                directive.GetArgumentValue(DirectiveNames.Defer.Arguments.If),
                variables,
                defaultValue: true);
    }
}
