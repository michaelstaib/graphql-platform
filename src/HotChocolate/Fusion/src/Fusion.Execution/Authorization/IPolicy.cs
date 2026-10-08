namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Evaluates a single policy for all of its occurrences in an operation.
/// </summary>
/// <remarks>
/// Instances are shared across requests. Per-request state must only be read from the
/// <see cref="PolicyEvaluationContext"/>.
/// </remarks>
public interface IPolicy
{
    /// <summary>
    /// Gets a value that indicates whether a subscription asks the policy again for every event
    /// against the current principal. <c>false</c> keeps the decision made when the subscription started.
    /// </summary>
    bool ReevaluatesPerEvent { get; }

    /// <summary>
    /// Answers the entries of the context through its <c>Allow</c> and <c>Deny</c> methods.
    /// Entries that are not answered stay <see cref="PolicyOutcome.Unanswered"/>.
    /// </summary>
    /// <param name="context">
    /// The evaluation context.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that signals that the request was aborted.
    /// </param>
    ValueTask EvaluateAsync(
        PolicyEvaluationContext context,
        CancellationToken cancellationToken);
}
