using System.Collections.Frozen;
using System.Globalization;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Pipeline;
using HotChocolate.Fusion.Policies.Audit;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Policies;

/// <summary>
/// The policy state of one protected subscription, which decides per event with the
/// policies that reevaluate and ends the stream when the token of the principal expires.
/// </summary>
internal sealed class SubscriptionPolicies
{
    private const string ExpiryClaimType = "exp";
    private const string ExpiredReason = "expired";

    private readonly PolicyEvaluator _evaluator;
    private readonly IAuditTrail _trail;
    private readonly AuditScopeInfo _info;
    private readonly bool _recordsAudit;
    private readonly OperationPlan _plan;
    private readonly PolicyPlan _policies;
    private readonly IVariableValueCollection _variables;
    private readonly IInputType _booleanType;
    private readonly TimeProvider _timeProvider;
    private readonly FrozenSet<PolicyDescriptor> _startDenied;
    private FrozenSet<PolicyDescriptor> _deniedDescriptors;

    /// <summary>
    /// Initializes a new instance of <see cref="SubscriptionPolicies"/>.
    /// </summary>
    /// <param name="evaluator">
    /// The evaluator that asks the policies.
    /// </param>
    /// <param name="scope">
    /// The audit scope of the decision made when the subscription started.
    /// </param>
    /// <param name="plan">
    /// The plan of the subscription.
    /// </param>
    /// <param name="policies">
    /// The policy requirements of the subscription.
    /// </param>
    /// <param name="variables">
    /// The coerced variable values of the subscription without the synthetic variables.
    /// </param>
    /// <param name="deniedDescriptors">
    /// The descriptors denied when the subscription started.
    /// </param>
    /// <param name="booleanType">
    /// The schema's Boolean type.
    /// </param>
    /// <param name="timeProvider">
    /// The time provider that measures the expiry of the token.
    /// </param>
    public SubscriptionPolicies(
        PolicyEvaluator evaluator,
        IAuditScope scope,
        OperationPlan plan,
        PolicyPlan policies,
        IVariableValueCollection variables,
        FrozenSet<PolicyDescriptor> deniedDescriptors,
        IInputType booleanType,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(deniedDescriptors);
        ArgumentNullException.ThrowIfNull(booleanType);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _evaluator = evaluator;
        _trail = scope.Trail;
        _info = scope.Info;
        _recordsAudit = scope.IsRecording;
        _plan = plan;
        _policies = policies;
        _variables = variables;
        _startDenied = deniedDescriptors;
        _deniedDescriptors = deniedDescriptors;
        _booleanType = booleanType;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets a value that indicates whether the subscription asks policies again for every event.
    /// </summary>
    public bool ReevaluatesPerEvent => _policies.HasReevaluatedPolicies;

    /// <summary>
    /// Gets the time at which the token of the principal expires, or <c>null</c> if the principal
    /// carries no expiry.
    /// </summary>
    /// <param name="user">
    /// The principal to inspect.
    /// </param>
    public static DateTimeOffset? GetExpiry(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindFirst(ExpiryClaimType) is { Value: var value }
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }

    /// <summary>
    /// Asks the policies that reevaluate again for the current principal. A change of the denied
    /// descriptors is committed in an audit scope and sets the variable values and the policy
    /// decisions for the next event on the operation plan context.
    /// </summary>
    /// <param name="context">
    /// The operation plan context of the subscription.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that bounds the evaluation of the event.
    /// </param>
    public async ValueTask ReevaluateAsync(
        OperationPlanContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestContext = context.RequestContext;
        var user = OperationPolicyMiddleware.GetUser(requestContext);
        var buffer = _recordsAudit ? new BufferedAuditScope(_trail, _info, user) : null;
        var scope = buffer ?? (IAuditScope)NoOpAuditScope.Instance;
        PolicyEvaluation evaluation;

        try
        {
            evaluation = await _evaluator.EvaluateAsync(
                requestContext,
                user,
                _plan,
                _policies,
                _variables,
                scope,
                _info.VariableSetIndex,
                _startDenied,
                cancellationToken);
        }
        catch (Exception ex) when (PolicyEvaluator.IsFault(ex, cancellationToken))
        {
            if (buffer is not null)
            {
                await _evaluator.CommitFaultedScopeAsync(requestContext, buffer.Replay(), ex);
            }

            if (ex is OperationCanceledException)
            {
                throw ThrowHelper.OperationPolicyFaulted(ex);
            }

            throw;
        }

        if (_deniedDescriptors.SetEquals(evaluation.DeniedDescriptors))
        {
            return;
        }

        if (buffer is not null)
        {
            try
            {
                await buffer.Replay().CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException ex) when (PolicyEvaluator.IsFault(ex, cancellationToken))
            {
                throw ThrowHelper.OperationPolicyFaulted(ex);
            }
        }

        _deniedDescriptors = evaluation.DeniedDescriptors;

        context.SetVariables(
            PolicyVariableValues.Create(
                _variables,
                _policies.Variables,
                evaluation.Decisions,
                _booleanType),
            evaluation.Decisions);
    }

    /// <summary>
    /// Returns the events of the subscription, ending when the token of the principal expires without
    /// renewal. The ending marks the result as requesting 401 Unauthorized.
    /// </summary>
    /// <param name="events">
    /// The events of the subscription.
    /// </param>
    /// <param name="result">
    /// The result that delivers the events.
    /// </param>
    /// <param name="context">
    /// The request context of the subscription.
    /// </param>
    /// <param name="expiry">
    /// The expiry of the token when the subscription started.
    /// </param>
    /// <param name="reportFailure">
    /// The callback that receives the failure to commit the audit scope of the expiry.
    /// </param>
    public IAsyncEnumerable<EventMessageResult> EndOnExpiry(
        IAsyncEnumerable<EventMessageResult> events,
        IExecutionResult result,
        RequestContext context,
        DateTimeOffset expiry,
        Action<Exception> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reportFailure);

        return new ExpiringEventStream(events, result, this, context, expiry, reportFailure, _timeProvider);
    }

    /// <summary>
    /// Returns the expiry of the token the principal of the request holds now, or <c>null</c> if
    /// the principal carries none.
    /// </summary>
    /// <param name="context">
    /// The request context of the subscription.
    /// </param>
    internal DateTimeOffset? GetCurrentExpiry(RequestContext context)
        => GetExpiry(OperationPolicyMiddleware.GetUser(context));

    /// <summary>
    /// Commits an audit scope that records the end of the stream because the token expired.
    /// </summary>
    /// <param name="context">
    /// The request context of the subscription.
    /// </param>
    /// <param name="reportFailure">
    /// The callback that receives a failure to commit the scope.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that signals that the request was aborted.
    /// </param>
    internal async ValueTask RecordExpiryAsync(
        RequestContext context,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        if (!_recordsAudit)
        {
            return;
        }

        try
        {
            var scope = _trail.BeginRequest(_info, OperationPolicyMiddleware.GetUser(context));

            _evaluator.RecordDenied(scope, _plan, _policies, _variables, ExpiredReason);
            await scope.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PolicyEvaluator.IsFault(ex, cancellationToken))
        {
            reportFailure(ex);
        }
    }
}
