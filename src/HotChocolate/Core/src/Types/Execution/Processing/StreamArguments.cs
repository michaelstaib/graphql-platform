using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Execution.Processing;

/// <summary>
/// The arguments of the <c>@stream</c> directive of a field selection, extracted when the operation is compiled.
/// </summary>
internal sealed class StreamArguments
{
    private readonly IValueNode? _if;
    private readonly IValueNode? _initialCount;
    private readonly IValueNode? _label;

    private StreamArguments(IValueNode? @if, IValueNode? initialCount, IValueNode? label)
    {
        _if = @if;
        _initialCount = initialCount;
        _label = label;
    }

    /// <summary>
    /// Gets a value indicating whether the <c>if</c> argument enables the directive for the specified variables.
    /// </summary>
    public bool IsEnabled(IVariableValueCollection variables)
        => _if is null
            || DirectiveIfCondition.IsEnabled(DirectiveNames.Stream.Name, _if, variables, defaultValue: true);

    /// <summary>
    /// Gets the number of list items that are delivered with the parent payload.
    /// </summary>
    public int GetInitialCount(IVariableValueCollection variables)
    {
        if (_initialCount is VariableNode variable)
        {
            return variables.TryGetValue<IntValueNode>(variable.Name.Value, out var variableValue)
                ? variableValue.ToInt32()
                : 0;
        }

        return _initialCount is IntValueNode intValue ? intValue.ToInt32() : 0;
    }

    /// <summary>
    /// Gets the label of the directive, or <c>null</c> if none was specified.
    /// </summary>
    public string? GetLabel(IVariableValueCollection variables)
    {
        if (_label is VariableNode variable)
        {
            return variables.TryGetValue<StringValueNode>(variable.Name.Value, out var variableValue)
                ? variableValue.Value
                : null;
        }

        return _label is StringValueNode stringValue ? stringValue.Value : null;
    }

    /// <summary>
    /// Gets the arguments of the first <c>@stream</c> directive of the specified syntax nodes, or <c>null</c>
    /// if there is none or its <c>if</c> argument is the literal <c>false</c>.
    /// </summary>
    public static StreamArguments? Create(FieldSelectionNode[] syntaxNodes)
    {
        foreach (var syntaxNode in syntaxNodes)
        {
            foreach (var directive in syntaxNode.Node.Directives)
            {
                if (directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal))
                {
                    return Create(directive);
                }
            }
        }

        return null;
    }

    private static StreamArguments? Create(DirectiveNode directive)
    {
        IValueNode? @if = null;
        IValueNode? initialCount = null;
        IValueNode? label = null;

        foreach (var argument in directive.Arguments)
        {
            var name = argument.Name.Value;

            if (name.Equals(DirectiveNames.Stream.Arguments.If, StringComparison.Ordinal))
            {
                switch (argument.Value)
                {
                    case BooleanValueNode { Value: false }:
                        return null;

                    case BooleanValueNode { Value: true }:
                        break;

                    default:
                        @if = argument.Value;
                        break;
                }
            }
            else if (name.Equals(DirectiveNames.Stream.Arguments.InitialCount, StringComparison.Ordinal))
            {
                initialCount = argument.Value;
            }
            else if (name.Equals(DirectiveNames.Stream.Arguments.Label, StringComparison.Ordinal))
            {
                label = argument.Value;
            }
        }

        return new StreamArguments(@if, initialCount, label);
    }
}
