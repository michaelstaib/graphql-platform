using System.Collections.Frozen;
using HotChocolate.Fusion.Policies;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// The policy state that is shared by all operations of one plan: the pinned policies
/// and the allocation of the synthetic variable names.
/// </summary>
internal sealed class PolicyPlanContext
{
    private const string VariableNamePrefix = "__fusion_policy_";

    private readonly IPolicyResolver _resolver;
    private readonly HashSet<string> _reservedNames = new(StringComparer.Ordinal);
    private readonly Dictionary<(string DirectiveName, string? PolicyName), IPolicy> _policies = [];
    private int _lastVariableId;

    public PolicyPlanContext(
        IPolicyResolver resolver,
        FrozenSet<string> protectedFieldNames,
        OperationDefinitionNode operationDefinition)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(protectedFieldNames);
        ArgumentNullException.ThrowIfNull(operationDefinition);

        _resolver = resolver;
        ProtectedFieldNames = protectedFieldNames;

        foreach (var variableDefinition in operationDefinition.VariableDefinitions)
        {
            _reservedNames.Add(variableDefinition.Variable.Name.Value);
        }
    }

    /// <summary>
    /// Gets the names of all fields of the schema that carry a policy requirement.
    /// </summary>
    public FrozenSet<string> ProtectedFieldNames { get; }

    public IPolicy GetPolicy(string directiveName, string? policyName)
    {
        if (!_policies.TryGetValue((directiveName, policyName), out var policy))
        {
            policy = _resolver.Resolve(policyName ?? string.Empty, directiveName)
                ?? new UnresolvedPolicy(directiveName, policyName);
            _policies.Add((directiveName, policyName), policy);
        }

        return policy;
    }

    public string NextVariableName()
    {
        string name;

        do
        {
            name = VariableNamePrefix + ++_lastVariableId;
        }
        while (_reservedNames.Contains(name));

        return name;
    }
}
