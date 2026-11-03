using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

internal static class SkipIncludeConditions
{
    /// <summary>
    /// Creates an <see cref="ExecutionNodeCondition"/> for each <c>@skip</c> and <c>@include</c>
    /// directive whose <c>if</c> argument is a variable. Returns <c>null</c> when there are none.
    /// </summary>
    public static ExecutionNodeCondition[]? Extract(IReadOnlyList<DirectiveNode> directives)
    {
        List<ExecutionNodeCondition>? conditions = null;

        foreach (var directive in directives)
        {
            var passingValue = directive.Name.Value switch
            {
                "skip" => false,
                "include" => true,
                _ => (bool?)null
            };

            if (passingValue.HasValue
                && directive.Arguments.Count > 0
                && directive.Arguments[0].Value is VariableNode variable)
            {
                conditions ??= [];
                conditions.Add(new ExecutionNodeCondition
                {
                    VariableName = variable.Name.Value,
                    PassingValue = passingValue.Value,
                    Directive = directive
                });
            }
        }

        return conditions?.ToArray();
    }
}
