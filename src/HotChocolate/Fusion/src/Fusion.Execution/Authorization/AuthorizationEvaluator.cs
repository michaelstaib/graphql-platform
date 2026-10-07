using System.Collections.Immutable;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Evaluates the policies of an operation for one variable set.
/// </summary>
internal sealed class AuthorizationEvaluator
{
    private static readonly ImmutableDictionary<string, object?> s_noArguments = [];

    private readonly FusionAuthorizationOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthorizationEvaluator"/>.
    /// </summary>
    /// <param name="options">
    /// The authorization options.
    /// </param>
    public AuthorizationEvaluator(FusionAuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>
    /// Evaluates every policy once with all of its occurrences that are part of the variable set.
    /// </summary>
    /// <param name="context">
    /// The request context.
    /// </param>
    /// <param name="user">
    /// The principal of the request.
    /// </param>
    /// <param name="plan">
    /// The plan of the operation.
    /// </param>
    /// <param name="descriptors">
    /// The descriptors of the operation and its incremental plans.
    /// </param>
    /// <param name="variables">
    /// The coerced variable values of the variable set.
    /// </param>
    /// <param name="requestIndex">
    /// The index of the variable set within the request.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that signals that the request was aborted.
    /// </param>
    public async ValueTask<AuthorizationEvaluation> EvaluateAsync(
        RequestContext context,
        ClaimsPrincipal user,
        OperationPlan plan,
        ImmutableArray<PolicyDescriptor> descriptors,
        IVariableValueCollection variables,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(variables);

        var isAuthenticated = user.Identity?.IsAuthenticated is true;
        var occurrences = GetOccurrences(descriptors, variables);
        var deniedDescriptors = new HashSet<PolicyDescriptor>();
        var groups = new List<PolicyGroup>();
        var groupsByPolicy = new Dictionary<IPolicy, PolicyGroup>();

        foreach (var descriptor in occurrences)
        {
            // An anonymous principal never reaches the scope and named policies.
            if (!isAuthenticated && descriptor.DirectiveName != DirectiveNames.Authenticated.Name)
            {
                deniedDescriptors.Add(descriptor);
                continue;
            }

            if (!groupsByPolicy.TryGetValue(descriptor.Policy, out var group))
            {
                group = new PolicyGroup(descriptor.Policy);
                groupsByPolicy.Add(descriptor.Policy, group);
                groups.Add(group);
            }

            group.Add(descriptor, PolicyEvaluationOrder.Get(descriptor.DirectiveName));
        }

        foreach (var group in groups.OrderBy(static g => g.Order))
        {
            var entries = new PolicyEvaluationEntry[group.Descriptors.Count];

            for (var i = 0; i < entries.Length; i++)
            {
                var descriptor = group.Descriptors[i];
                entries[i] = new PolicyEvaluationEntry(
                    descriptor,
                    CoerceArguments((Selection)descriptor.Selection, variables));
            }

            var policyContext = new PolicyEvaluationContext(
                user,
                context.Features,
                context.RequestServices,
                plan,
                requestIndex,
                entries);

            try
            {
                await group.Policy.EvaluateAsync(policyContext, cancellationToken);
            }
            catch (InvalidOperationException ex) when (group.Policy is UnresolvedPolicy)
            {
                return new AuthorizationEvaluation(null, ex);
            }

            CollectDenied(policyContext, deniedDescriptors);
        }

        return new AuthorizationEvaluation(
            CreateDecisions(occurrences, deniedDescriptors, isAuthenticated),
            null);
    }

    private AuthorizationDecisions? CreateDecisions(
        ImmutableArray<PolicyDescriptor> occurrences,
        HashSet<PolicyDescriptor> deniedDescriptors,
        bool isAuthenticated)
    {
        if (deniedDescriptors.Count == 0)
        {
            return null;
        }

        var kind = isAuthenticated
            ? AuthorizationDenialKind.Unauthorized
            : AuthorizationDenialKind.Unauthenticated;
        var denials = ImmutableArray.CreateBuilder<SelectionDenial>();
        var indexBySelection = new Dictionary<Selection, int>();

        foreach (var descriptor in occurrences)
        {
            if (!deniedDescriptors.Contains(descriptor))
            {
                continue;
            }

            var selection = (Selection)descriptor.Selection;

            if (!indexBySelection.TryGetValue(selection, out var index))
            {
                indexBySelection.Add(selection, denials.Count);
                denials.Add(new SelectionDenial(selection, kind, descriptor));
            }
            else if (PolicyEvaluationOrder.Get(descriptor.DirectiveName)
                < PolicyEvaluationOrder.Get(denials[index].Descriptor.DirectiveName))
            {
                denials[index] = new SelectionDenial(selection, kind, descriptor);
            }
        }

        return new AuthorizationDecisions(
            denials.ToImmutable(),
            _options.DenyHandling,
            _options.EnableAttribution);
    }

    private static void CollectDenied(
        PolicyEvaluationContext policyContext,
        HashSet<PolicyDescriptor> deniedDescriptors)
    {
        var entries = policyContext.Entries;
        var verdicts = policyContext.Verdicts;

        for (var i = 0; i < entries.Length; i++)
        {
            if (verdicts[i].Outcome is not PolicyOutcome.Allowed)
            {
                deniedDescriptors.Add(entries[i].Descriptor);
            }
        }
    }

    private static ImmutableArray<PolicyDescriptor> GetOccurrences(
        ImmutableArray<PolicyDescriptor> descriptors,
        IVariableValueCollection variables)
    {
        var occurrences = ImmutableArray.CreateBuilder<PolicyDescriptor>(descriptors.Length);
        var flagsByOperation = new Dictionary<Operation, ConditionFlags>();

        foreach (var descriptor in descriptors)
        {
            if (IsReachable((Selection)descriptor.Selection, variables, flagsByOperation))
            {
                occurrences.Add(descriptor);
            }
        }

        return occurrences.ToImmutable();
    }

    private static bool IsReachable(
        Selection selection,
        IVariableValueCollection variables,
        Dictionary<Operation, ConditionFlags> flagsByOperation)
    {
        var operation = selection.DeclaringSelectionSet.DeclaringOperation;

        if (!flagsByOperation.TryGetValue(operation, out var flags))
        {
            flags = operation.CreateIncludeConditionFlags(variables);
            flagsByOperation.Add(operation, flags);
        }

        for (var current = (Selection?)selection;
            current is not null;
            current = current.DeclaringSelectionSet.DeclaringSelection)
        {
            if (!current.IsIncluded(flags))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableDictionary<string, object?> CoerceArguments(
        Selection selection,
        IVariableValueCollection variables)
    {
        var definitions = selection.Field.Arguments;

        if (definitions.Count == 0)
        {
            return s_noArguments;
        }

        var syntaxNode = selection.SyntaxNodes[0].Node;
        var arguments = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            IValueNode? value = null;

            foreach (var argument in syntaxNode.Arguments)
            {
                if (argument.Name.Value.Equals(definition.Name, StringComparison.Ordinal))
                {
                    value = argument.Value;
                    break;
                }
            }

            if (value is VariableNode variable)
            {
                variables.TryGetValue(variable.Name.Value, out value);
            }

            arguments.Add(definition.Name, value ?? definition.DefaultValue ?? NullValueNode.Default);
        }

        return arguments.ToImmutable();
    }

    private sealed class PolicyGroup(IPolicy policy)
    {
        public IPolicy Policy { get; } = policy;

        public int Order { get; private set; } = int.MaxValue;

        public ImmutableArray<PolicyDescriptor>.Builder Descriptors { get; } =
            ImmutableArray.CreateBuilder<PolicyDescriptor>();

        public void Add(PolicyDescriptor descriptor, int order)
        {
            Descriptors.Add(descriptor);
            Order = Math.Min(Order, order);
        }
    }
}
