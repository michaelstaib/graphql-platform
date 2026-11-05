namespace HotChocolate.Fusion.Policies;

/// <summary>
/// The outcome of evaluating the policies of one variable set.
/// </summary>
/// <param name="Outcome">
/// The denied selections, or <c>null</c> if every selection is allowed.
/// </param>
/// <param name="Failure">
/// The failure of a policy that no provider resolved, or <c>null</c> if every policy was evaluated.
/// </param>
internal readonly record struct PolicyEvaluation(
    PolicyOutcome? Outcome,
    InvalidOperationException? Failure);
