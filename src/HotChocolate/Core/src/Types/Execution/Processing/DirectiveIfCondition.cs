using HotChocolate.Language;

namespace HotChocolate.Execution.Processing;

internal static class DirectiveIfCondition
{
    /// <summary>
    /// Evaluates a variable used for the <c>if</c> argument of <c>@defer</c> or <c>@stream</c>.
    /// A variable that was not provided yields the argument default <c>true</c>; a variable that
    /// is explicitly <c>null</c> raises an error.
    /// </summary>
    public static bool IsEnabled(
        string directiveName,
        string variableName,
        IVariableValueCollection variableValues)
    {
        if (!variableValues.TryGetValue<IValueNode>(variableName, out var value))
        {
            return true;
        }

        return value switch
        {
            BooleanValueNode boolean => boolean.Value,
            NullValueNode => throw ThrowHelper.DirectiveIfVariableIsNull(directiveName, variableName),
            _ => throw new InvalidOperationException($"The variable {variableName} has an invalid value.")
        };
    }
}
