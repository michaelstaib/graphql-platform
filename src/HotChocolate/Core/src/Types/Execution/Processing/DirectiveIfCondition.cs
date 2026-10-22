using HotChocolate.Language;

namespace HotChocolate.Execution.Processing;

internal static class DirectiveIfCondition
{
    /// <summary>
    /// Evaluates the variable used for the <c>if</c> argument of a conditional directive.
    /// A variable that was not provided yields <paramref name="defaultValue"/>, a variable
    /// that is explicitly <c>null</c> raises an error.
    /// </summary>
    public static bool IsEnabled(
        string directiveName,
        string variableName,
        IVariableValueCollection variableValues,
        bool defaultValue)
    {
        return variableValues.TryGetValue<IValueNode>(variableName, out var value)
            ? ToBoolean(directiveName, variableName, value)
            : defaultValue;
    }

    /// <summary>
    /// Evaluates the variable used for the <c>if</c> argument of a conditional directive.
    /// A variable that was not provided or that is explicitly <c>null</c> raises an error.
    /// </summary>
    public static bool IsEnabled(
        string directiveName,
        string variableName,
        IVariableValueCollection variableValues)
    {
        if (!variableValues.TryGetValue<IValueNode>(variableName, out var value))
        {
            throw ThrowHelper.DirectiveIfVariableNotProvided(directiveName, variableName);
        }

        return ToBoolean(directiveName, variableName, value);
    }

    private static bool ToBoolean(string directiveName, string variableName, IValueNode value)
    {
        return value switch
        {
            BooleanValueNode boolean => boolean.Value,
            NullValueNode => throw ThrowHelper.DirectiveIfVariableIsNull(directiveName, variableName),
            _ => throw ThrowHelper.DirectiveIfVariableInvalidValue(directiveName, variableName)
        };
    }
}
