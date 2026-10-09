using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Computes the values of the synthetic authorization variables of a variable set.
/// </summary>
internal static class AuthorizationVariableValues
{
    /// <summary>
    /// Creates the variable values of a variable set with a value for every synthetic variable.
    /// </summary>
    /// <param name="variableValues">
    /// The variable values of the variable set.
    /// </param>
    /// <param name="variables">
    /// The synthetic variables of the operation and its incremental plans.
    /// </param>
    /// <param name="decisions">
    /// The denied selections of the variable set, or <c>null</c> if every selection is allowed.
    /// </param>
    /// <param name="type">
    /// The <c>Boolean!</c> type of the variables.
    /// </param>
    public static VariableValueCollection Create(
        IVariableValueCollection variableValues,
        ImmutableArray<AuthorizationVariable> variables,
        AuthorizationDecisions? decisions,
        IInputType type)
    {
        ArgumentNullException.ThrowIfNull(variableValues);
        ArgumentNullException.ThrowIfNull(type);

        var values = new Dictionary<string, VariableValue>();

        foreach (var value in variableValues)
        {
            values[value.Name] = value;
        }

        var evaluated = new Dictionary<string, bool>(variables.Length, StringComparer.Ordinal);

        foreach (var variable in variables)
        {
            var value = Evaluate(variable, decisions, evaluated);

            values[variable.Name] = new VariableValue(
                variable.Name,
                type,
                value ? BooleanValueNode.True : BooleanValueNode.False);
        }

        return new VariableValueCollection(values, decisions);
    }

    private static bool Evaluate(
        AuthorizationVariable variable,
        AuthorizationDecisions? decisions,
        Dictionary<string, bool> evaluated)
    {
        if (evaluated.TryGetValue(variable.Name, out var value))
        {
            return value;
        }

        if (variable.Operands.IsEmpty)
        {
            value = false;

            if (decisions is not null)
            {
                foreach (var selection in variable.Selections)
                {
                    if (decisions.IsDenied(selection))
                    {
                        value = true;
                        break;
                    }
                }
            }
        }
        else
        {
            value = true;

            foreach (var operand in variable.Operands)
            {
                if (!Evaluate(operand, decisions, evaluated))
                {
                    value = false;
                    break;
                }
            }
        }

        evaluated.Add(variable.Name, value);
        return value;
    }
}
