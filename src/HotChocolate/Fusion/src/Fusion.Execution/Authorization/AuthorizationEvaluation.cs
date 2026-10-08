namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// The outcome of evaluating the policies of one variable set.
/// </summary>
/// <param name="Decisions">
/// The denied selections, or <c>null</c> if every selection is allowed.
/// </param>
/// <param name="Failure">
/// The failure of a policy that no provider resolved, or <c>null</c> if every policy was evaluated.
/// </param>
/// <param name="DeniedDescriptors">
/// The descriptors whose policy did not allow them, including those of a selection that another
/// policy group allowed.
/// </param>
internal readonly record struct AuthorizationEvaluation(
    AuthorizationDecisions? Decisions,
    InvalidOperationException? Failure,
    IReadOnlySet<PolicyDescriptor> DeniedDescriptors);
