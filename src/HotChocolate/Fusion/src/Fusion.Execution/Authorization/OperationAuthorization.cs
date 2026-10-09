using System.Collections.Immutable;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// The authorization requirements of the selections of an operation and the synthetic variables
/// that skip denied selections in source schema requests.
/// </summary>
public sealed class OperationAuthorization
{
    internal OperationAuthorization(
        ImmutableArray<PolicyDescriptor> descriptors,
        ImmutableArray<AuthorizationVariable> variables)
    {
        Descriptors = descriptors;
        Variables = variables;
        HasUnresolvedPolicies = ContainsUnresolvedPolicy(descriptors);
        HasReevaluatedPolicies = ContainsReevaluatedPolicy(descriptors);
    }

    /// <summary>
    /// Gets one descriptor for every directive occurrence of every protected selection.
    /// </summary>
    public ImmutableArray<PolicyDescriptor> Descriptors { get; }

    /// <summary>
    /// Gets the synthetic variables in the order they were allocated.
    /// </summary>
    public ImmutableArray<AuthorizationVariable> Variables { get; }

    /// <summary>
    /// Gets a value that indicates whether any descriptor refers to a policy name that no
    /// provider resolved.
    /// </summary>
    internal bool HasUnresolvedPolicies { get; }

    /// <summary>
    /// Gets a value that indicates whether any descriptor refers to a policy that a subscription
    /// asks again for every event.
    /// </summary>
    internal bool HasReevaluatedPolicies { get; }

    private static bool ContainsUnresolvedPolicy(ImmutableArray<PolicyDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Policy is UnresolvedPolicy)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsReevaluatedPolicy(ImmutableArray<PolicyDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Policy.ReevaluatesPerEvent)
            {
                return true;
            }
        }

        return false;
    }
}
