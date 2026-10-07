using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution;

internal sealed class VariableValueCollection : IVariableValueCollection
{
    private readonly Dictionary<string, VariableValue> _coercedValues;

    public VariableValueCollection(Dictionary<string, VariableValue> coercedValues)
        : this(coercedValues, null)
    {
    }

    public VariableValueCollection(
        Dictionary<string, VariableValue> coercedValues,
        AuthorizationDecisions? authorizationDecisions)
    {
        ArgumentNullException.ThrowIfNull(coercedValues);

        _coercedValues = coercedValues;
        AuthorizationDecisions = authorizationDecisions;
    }

    public static VariableValueCollection Empty { get; } = new([]);

    public bool IsEmpty => _coercedValues.Count == 0;

    public AuthorizationDecisions? AuthorizationDecisions { get; }

    public T GetValue<T>(string name) where T : IValueNode
    {
        if (TryGetValue(name, out T? value))
        {
            return value;
        }

        if (_coercedValues.ContainsKey(name))
        {
            throw ThrowHelper.VariableNotOfType(name, typeof(T));
        }

        throw ThrowHelper.VariableNotFound(name);
    }

    public bool TryGetValue<T>(string name, [NotNullWhen(true)] out T? value) where T : IValueNode
    {
        if (_coercedValues.TryGetValue(name, out var variableValue)
            && variableValue.Value is T casted)
        {
            value = casted;
            return true;
        }

        value = default;
        return false;
    }

    public IEnumerator<VariableValue> GetEnumerator()
        => _coercedValues.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
