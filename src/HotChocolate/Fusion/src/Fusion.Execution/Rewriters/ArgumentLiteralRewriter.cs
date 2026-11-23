using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Execution.Rewriters;

/// <summary>
/// Replaces the literal values of field arguments in a normalized operation with variables.
/// </summary>
internal sealed class ArgumentLiteralRewriter
{
    private const string VariableNamePrefix = "__fusion_literal_";
    private const string IntrospectionPrefix = "__";

    private readonly ISchemaDefinition _schema;

    public ArgumentLiteralRewriter(ISchemaDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _schema = schema;
    }

    /// <summary>
    /// Replaces each field argument value of <paramref name="operation"/> that is a literal
    /// without variables with a variable of the declared type of the argument. Arguments of
    /// directives and of introspection fields keep their values.
    /// </summary>
    /// <param name="operation">
    /// The normalized operation.
    /// </param>
    /// <param name="rewritten">
    /// The operation with the replaced argument values and the definitions of the added variables.
    /// </param>
    /// <param name="values">
    /// The values of the added variables.
    /// </param>
    /// <returns>
    /// <c>true</c> if at least one argument value was replaced, otherwise <c>false</c>.
    /// </returns>
    public bool TryRewrite(
        OperationDefinitionNode operation,
        [NotNullWhen(true)] out OperationDefinitionNode? rewritten,
        out ImmutableArray<VariableValue> values)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var rootType = _schema.GetOperationType(operation.Operation);
        var context = new Context(operation.VariableDefinitions);
        SelectionSetNode selectionSet;

        do
        {
            // Two fields with the same response name must keep equal arguments so they can still
            // be merged. A rewrite that had to leave one of them untouched starts over and leaves
            // the argument of all of them untouched.
            context.Reset();
            selectionSet = RewriteSelectionSet(operation.SelectionSet, rootType, context);
        } while (context.HasConflict);

        if (context.Values.Count == 0)
        {
            rewritten = null;
            values = [];
            return false;
        }

        rewritten = operation
            .WithVariableDefinitions([.. operation.VariableDefinitions, .. context.Definitions])
            .WithSelectionSet(selectionSet);
        values = [.. context.Values];
        return true;
    }

    private SelectionSetNode RewriteSelectionSet(
        SelectionSetNode selectionSet,
        ITypeDefinition? type,
        Context context)
    {
        var selections = selectionSet.Selections;
        ISelectionNode[]? rewritten = null;

        for (var i = 0; i < selections.Count; i++)
        {
            var selection = selections[i];
            var rewrittenSelection = selection switch
            {
                FieldNode field => RewriteField(field, type, context),
                InlineFragmentNode inlineFragment => RewriteInlineFragment(inlineFragment, type, context),
                _ => selection
            };

            if (ReferenceEquals(selection, rewrittenSelection))
            {
                continue;
            }

            rewritten ??= [.. selections];
            rewritten[i] = rewrittenSelection;
        }

        return rewritten is null ? selectionSet : selectionSet.WithSelections(rewritten);
    }

    private FieldNode RewriteField(FieldNode field, ITypeDefinition? type, Context context)
    {
        var fieldName = field.Name.Value;

        // Introspection fields are resolved by the gateway, which reads their argument values
        // from the operation.
        if (fieldName.StartsWith(IntrospectionPrefix, StringComparison.Ordinal)
            || type is not IComplexTypeDefinition complexType
            || !complexType.Fields.TryGetField(fieldName, out var fieldDefinition))
        {
            return field;
        }

        var rewrittenField = field;

        if (field.Arguments.Count > 0)
        {
            var arguments = RewriteArguments(field, fieldDefinition, context);

            if (!ReferenceEquals(arguments, field.Arguments))
            {
                rewrittenField = rewrittenField.WithArguments(arguments);
            }
        }

        if (field.SelectionSet is { } selectionSet)
        {
            var rewrittenSelectionSet = RewriteSelectionSet(
                selectionSet,
                fieldDefinition.Type.AsTypeDefinition(),
                context);

            if (!ReferenceEquals(selectionSet, rewrittenSelectionSet))
            {
                rewrittenField = rewrittenField.WithSelectionSet(rewrittenSelectionSet);
            }
        }

        return rewrittenField;
    }

    private static IReadOnlyList<ArgumentNode> RewriteArguments(
        FieldNode field,
        IOutputFieldDefinition fieldDefinition,
        Context context)
    {
        var arguments = field.Arguments;
        var responseName = field.Alias?.Value ?? field.Name.Value;
        ArgumentNode[]? rewritten = null;

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];

            if (ContainsVariable(argument.Value)
                || !fieldDefinition.Arguments.TryGetField(argument.Name.Value, out var argumentDefinition)
                || !context.TryGetVariable(
                    responseName,
                    argument.Name.Value,
                    argument.Value,
                    argumentDefinition.Type,
                    out var variable))
            {
                continue;
            }

            rewritten ??= [.. arguments];
            rewritten[i] = argument.WithValue(variable);
        }

        return rewritten ?? arguments;
    }

    private InlineFragmentNode RewriteInlineFragment(
        InlineFragmentNode inlineFragment,
        ITypeDefinition? type,
        Context context)
    {
        if (inlineFragment.TypeCondition is { } typeCondition)
        {
            type = _schema.Types.TryGetType(typeCondition.Name.Value, out var typeDefinition)
                ? typeDefinition
                : null;
        }

        var selectionSet = RewriteSelectionSet(inlineFragment.SelectionSet, type, context);

        return ReferenceEquals(selectionSet, inlineFragment.SelectionSet)
            ? inlineFragment
            : inlineFragment.WithSelectionSet(selectionSet);
    }

    private static bool ContainsVariable(IValueNode value)
    {
        switch (value)
        {
            case VariableNode:
                return true;

            case ListValueNode list:
                for (var i = 0; i < list.Items.Count; i++)
                {
                    if (ContainsVariable(list.Items[i]))
                    {
                        return true;
                    }
                }

                return false;

            case ObjectValueNode obj:
                for (var i = 0; i < obj.Fields.Count; i++)
                {
                    if (ContainsVariable(obj.Fields[i].Value))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private sealed class Context
    {
        private readonly HashSet<string> _variableNames = new(StringComparer.Ordinal);
        private readonly Dictionary<Literal, VariableNode> _variables = new(LiteralComparer.Instance);
        private readonly Dictionary<ArgumentUsage, ITypeNode> _usages = new(ArgumentUsageComparer.Instance);
        private readonly HashSet<(string ResponseName, string ArgumentName)> _excluded = [];
        private int _nextVariableId;

        public Context(IReadOnlyList<VariableDefinitionNode> variableDefinitions)
        {
            for (var i = 0; i < variableDefinitions.Count; i++)
            {
                _variableNames.Add(variableDefinitions[i].Variable.Name.Value);
            }
        }

        public List<VariableDefinitionNode> Definitions { get; } = [];

        public List<VariableValue> Values { get; } = [];

        public bool HasConflict { get; private set; }

        public void Reset()
        {
            _variables.Clear();
            _usages.Clear();
            _nextVariableId = 0;
            Definitions.Clear();
            Values.Clear();
            HasConflict = false;
        }

        public bool TryGetVariable(
            string responseName,
            string argumentName,
            IValueNode value,
            IInputType type,
            [NotNullWhen(true)] out VariableNode? variable)
        {
            if (_excluded.Contains((responseName, argumentName)))
            {
                variable = null;
                return false;
            }

            var typeNode = type.ToTypeNode();
            var usage = new ArgumentUsage(responseName, argumentName, value);

            if (!_usages.TryGetValue(usage, out var usedTypeNode))
            {
                _usages.Add(usage, typeNode);
            }
            else if (!SyntaxComparer.BySyntax.Equals(usedTypeNode, typeNode))
            {
                // The same value needs variables of two types here, which would give two fields
                // with the same response name different arguments.
                _excluded.Add((responseName, argumentName));
                HasConflict = true;
                variable = null;
                return false;
            }

            var literal = new Literal(value, typeNode);

            if (!_variables.TryGetValue(literal, out variable))
            {
                variable = new VariableNode(CreateVariableName());
                _variables.Add(literal, variable);
                Definitions.Add(new VariableDefinitionNode(null, variable, null, typeNode, null, []));
                Values.Add(new VariableValue(variable.Name.Value, type, value));
            }

            return true;
        }

        private string CreateVariableName()
        {
            string name;

            do
            {
                name = VariableNamePrefix + _nextVariableId++;
            } while (_variableNames.Contains(name));

            return name;
        }
    }

    private readonly record struct Literal(IValueNode Value, ITypeNode Type);

    private readonly record struct ArgumentUsage(string ResponseName, string ArgumentName, IValueNode Value);

    private sealed class LiteralComparer : IEqualityComparer<Literal>
    {
        public static readonly LiteralComparer Instance = new();

        public bool Equals(Literal x, Literal y)
            => SyntaxComparer.BySyntax.Equals(x.Value, y.Value)
                && SyntaxComparer.BySyntax.Equals(x.Type, y.Type);

        public int GetHashCode(Literal obj)
            => HashCode.Combine(
                SyntaxComparer.BySyntax.GetHashCode(obj.Value),
                SyntaxComparer.BySyntax.GetHashCode(obj.Type));
    }

    private sealed class ArgumentUsageComparer : IEqualityComparer<ArgumentUsage>
    {
        public static readonly ArgumentUsageComparer Instance = new();

        public bool Equals(ArgumentUsage x, ArgumentUsage y)
            => x.ResponseName.Equals(y.ResponseName, StringComparison.Ordinal)
                && x.ArgumentName.Equals(y.ArgumentName, StringComparison.Ordinal)
                && SyntaxComparer.BySyntax.Equals(x.Value, y.Value);

        public int GetHashCode(ArgumentUsage obj)
            => HashCode.Combine(
                obj.ResponseName,
                obj.ArgumentName,
                SyntaxComparer.BySyntax.GetHashCode(obj.Value));
    }
}
