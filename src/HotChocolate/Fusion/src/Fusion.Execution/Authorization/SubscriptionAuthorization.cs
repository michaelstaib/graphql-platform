using System.Collections.Frozen;
using System.Globalization;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization.Audit;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Pipeline;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// The authorization state of one protected subscription, which decides per event with the
/// policies that reevaluate and ends the stream when the token of the principal expires.
/// </summary>
internal sealed class SubscriptionAuthorization
{
    private const string ExpiryClaimType = "exp";
    private const string ExpiredReason = "expired";

    private readonly AuthorizationEvaluator _evaluator;
    private readonly IAuditTrail _trail;
    private readonly AuditScopeInfo _info;
    private readonly bool _recordsAudit;
    private readonly OperationPlan _plan;
    private readonly OperationAuthorization _authorization;
    private readonly IVariableValueCollection _variables;
    private readonly IInputType _variableType;
    private readonly TimeProvider _timeProvider;
    private readonly FrozenSet<PolicyDescriptor> _startDenied;
    private FrozenSet<PolicyDescriptor> _deniedDescriptors;
    private VariableValueCollection _current;

    /// <summary>
    /// Initializes a new instance of <see cref="SubscriptionAuthorization"/>.
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
    /// <param name="authorization">
    /// The authorization requirements of the subscription.
    /// </param>
    /// <param name="variables">
    /// The coerced variable values of the subscription before the synthetic variables were added.
    /// </param>
    /// <param name="current">
    /// The variable values of the subscription including the synthetic variables.
    /// </param>
    /// <param name="deniedDescriptors">
    /// The descriptors denied when the subscription started.
    /// </param>
    /// <param name="variableType">
    /// The <c>Boolean!</c> type of the synthetic variables.
    /// </param>
    /// <param name="timeProvider">
    /// The time provider that measures the expiry of the token.
    /// </param>
    public SubscriptionAuthorization(
        AuthorizationEvaluator evaluator,
        IAuditScope scope,
        OperationPlan plan,
        OperationAuthorization authorization,
        IVariableValueCollection variables,
        VariableValueCollection current,
        FrozenSet<PolicyDescriptor> deniedDescriptors,
        IInputType variableType,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(deniedDescriptors);
        ArgumentNullException.ThrowIfNull(variableType);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _evaluator = evaluator;
        _trail = scope.Trail;
        _info = scope.Info;
        _recordsAudit = scope.IsRecording;
        _plan = plan;
        _authorization = authorization;
        _variables = variables;
        _current = current;
        _startDenied = deniedDescriptors;
        _deniedDescriptors = deniedDescriptors;
        _variableType = variableType;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets a value that indicates whether the subscription asks policies again for every event.
    /// </summary>
    public bool ReevaluatesPerEvent => _authorization.HasReevaluatedPolicies;

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
    /// Asks the policies that reevaluate again for the current principal and returns the variable
    /// values for the next event. A change of the denied descriptors is committed in an audit
    /// scope before the method returns.
    /// </summary>
    /// <param name="context">
    /// The request context of the subscription.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that bounds the evaluation of the event.
    /// </param>
    public async ValueTask<VariableValueCollection> ReevaluateAsync(
        RequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = OperationAuthorizationMiddleware.GetUser(context);
        var buffer = _recordsAudit ? new BufferedAuditScope(_trail, _info, user) : null;
        var scope = buffer ?? (IAuditScope)NoOpAuditScope.Instance;
        AuthorizationEvaluation evaluation;

        try
        {
            evaluation = await _evaluator.EvaluateAsync(
                context,
                user,
                _plan,
                _authorization,
                _variables,
                scope,
                _info.VariableSetIndex,
                _startDenied,
                cancellationToken);
        }
        catch (Exception ex) when (AuthorizationEvaluator.IsFault(ex, cancellationToken))
        {
            if (buffer is not null)
            {
                await _evaluator.CommitFaultedScopeAsync(context, buffer.Replay(), ex);
            }

            _evaluator.ReportError(context, ex);

            if (ex is OperationCanceledException)
            {
                throw ThrowHelper.OperationAuthorizationFaulted(ex);
            }

            throw;
        }

        if (_deniedDescriptors.SetEquals(evaluation.DeniedDescriptors))
        {
            return _current;
        }

        if (buffer is not null)
        {
            try
            {
                await buffer.Replay().CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (AuthorizationEvaluator.IsFault(ex, cancellationToken))
            {
                _evaluator.ReportError(context, ex);
                throw;
            }
        }

        _deniedDescriptors = evaluation.DeniedDescriptors;
        _current = AuthorizationVariableValues.Create(
            _variables,
            _authorization.Variables,
            evaluation.Decisions,
            _variableType);

        return _current;
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
    public IAsyncEnumerable<EventMessageResult> EndOnExpiry(
        IAsyncEnumerable<EventMessageResult> events,
        IExecutionResult result,
        RequestContext context,
        DateTimeOffset expiry)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);

        return new ExpiringEventStream(events, result, this, context, expiry, _timeProvider);
    }

    /// <summary>
    /// Returns the expiry of the token the principal of the request holds now, or <c>null</c> if
    /// the principal carries none.
    /// </summary>
    /// <param name="context">
    /// The request context of the subscription.
    /// </param>
    internal DateTimeOffset? GetCurrentExpiry(RequestContext context)
        => GetExpiry(OperationAuthorizationMiddleware.GetUser(context));

    /// <summary>
    /// Commits an audit scope that records the end of the stream because the token expired.
    /// </summary>
    /// <param name="context">
    /// The request context of the subscription.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that signals that the request was aborted.
    /// </param>
    internal async ValueTask RecordExpiryAsync(RequestContext context, CancellationToken cancellationToken)
    {
        if (!_recordsAudit)
        {
            return;
        }

        try
        {
            var scope = _trail.BeginRequest(_info, OperationAuthorizationMiddleware.GetUser(context));

            _evaluator.RecordDenied(scope, _plan, _authorization, _variables, ExpiredReason);
            await scope.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (AuthorizationEvaluator.IsFault(ex, cancellationToken))
        {
            _evaluator.ReportError(context, ex);
            throw;
        }
    }
}
