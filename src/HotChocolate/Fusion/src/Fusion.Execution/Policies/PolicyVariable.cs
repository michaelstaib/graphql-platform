using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;

namespace HotChocolate.Fusion.Policies;

/// <summary>
/// A synthetic Boolean variable of an operation plan that is <c>true</c> for a variable set
/// when the selections it gates are denied.
/// </summary>
public sealed class PolicyVariable
{
    internal PolicyVariable(
        string name,
        ImmutableArray<Selection> selections,
        ImmutableArray<PolicyVariable> operands)
    {
        Name = name;
        Selections = selections;
        Operands = operands;
    }

    /// <summary>
    /// Gets the name of the variable.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the protected selections the variable gates. The variable is <c>true</c> when any of
    /// them is denied. It is empty for variables that combine other variables.
    /// </summary>
    public ImmutableArray<Selection> Selections { get; }

    /// <summary>
    /// Gets the variables the variable combines. The variable is <c>true</c> when all of them are
    /// <c>true</c>. It is empty for variables that gate selections.
    /// </summary>
    public ImmutableArray<PolicyVariable> Operands { get; }

    /// <summary>
    /// Returns whether the variable is <c>true</c> for a variable set.
    /// </summary>
    /// <param name="outcome">
    /// The denied selections of the variable set, or <c>null</c> if every selection is allowed.
    /// </param>
    internal bool IsTrue(PolicyOutcome? outcome)
    {
        if (Operands.IsEmpty)
        {
            if (outcome is null)
            {
                return false;
            }

            foreach (var selection in Selections)
            {
                if (outcome.IsDenied(selection))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var operand in Operands)
        {
            if (!operand.IsTrue(outcome))
            {
                return false;
            }
        }

        return true;
    }
}
