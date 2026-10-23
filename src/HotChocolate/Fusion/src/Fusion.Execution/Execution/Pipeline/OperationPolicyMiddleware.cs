using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Policies.Audit;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution.Pipeline;

internal sealed class OperationPolicyMiddleware
{
    private const string Key = "FusionOperationPolicyMiddleware";

    private readonly PolicyEvaluator _evaluator;
    private readonly IAuditProvider _auditProvider;
    private readonly FusionAuthorizationOptions _options;
    private readonly AuthenticationSchemeResolver _schemeResolver;
    private readonly IFusionExecutionDiagnosticEvents _diagnosticEvents;
    private readonly IInputType _booleanType;
    private readonly TimeProvider _timeProvider;

    private OperationPolicyMiddleware(
        PolicyEvaluator evaluator,
        IAuditProvider auditProvider,
        FusionAuthorizationOptions options,
        AuthenticationSchemeResolver schemeResolver,
        IFusionExecutionDiagnosticEvents diagnosticEvents,
        IInputType booleanType,
        TimeProvider timeProvider)
    {
        _evaluator = evaluator;
        _auditProvider = auditProvider;
        _options = options;
        _schemeResolver = schemeResolver;
        _diagnosticEvents = diagnosticEvents;
        _booleanType = booleanType;
        _timeProvider = timeProvider;
    }

    public ValueTask InvokeAsync(RequestContext context, RequestDelegate next)
    {
        var plan = context.GetOperationPlan() ?? throw ThrowHelper.OperationPolicyRequiresPlan();

        return plan.Policies is { } policies
            ? InvokeProtectedAsync(context, plan, policies, next)
            : next(context);
    }

    private async ValueTask InvokeProtectedAsync(
        RequestContext context,
        OperationPlan plan,
        PolicyPlan policies,
        RequestDelegate next)
    {
        var variableSets = context.VariableValues;

        if (variableSets.IsDefaultOrEmpty)
        {
            await next(context);
            return;
        }

        var user = GetUser(context);
        var trail = context.Features.TryGet(out AuditInvocation? auditInvocation)
            ? auditInvocation.GetOrCreateTrail(_auditProvider, context.RequestServices)
            : _auditProvider.CreateTrail(context.RequestServices);
        PolicyDecisions?[]? decisionSets = null;
        IVariableValueCollection[]? updatedVariableSets = null;
        var isSubscription = plan.Operation.Definition.Operation is OperationType.Subscription;

        if (isSubscription
            && SubscriptionPolicies.GetExpiry(user) is { } expiry
            && expiry <= _timeProvider.GetUtcNow())
        {
            await RefuseExpiredAsync(context, trail, user, plan, policies, variableSets);
            return;
        }

        for (var i = 0; i < variableSets.Length; i++)
        {
            var scope = trail.BeginRequest(
                new AuditScopeInfo(plan.Operation.Id, plan.Id, context.RequestIndex, i),
                user);

            PolicyEvaluation evaluation;

            try
            {
                evaluation = await _evaluator.EvaluateAsync(
                    context,
                    user,
                    plan,
                    policies,
                    variableSets[i],
                    scope,
                    i,
                    null,
                    context.RequestAborted);
            }
            catch (Exception ex) when (PolicyEvaluator.IsFault(ex, context.RequestAborted))
            {
                await _evaluator.CommitFaultedScopeAsync(context, scope, ex);

                if (ex is OperationCanceledException)
                {
                    throw ThrowHelper.OperationPolicyFaulted(ex);
                }

                throw;
            }

            var failure = evaluation.Failure;

            if (failure is not null)
            {
                scope.Fail(failure);
            }

            await scope.CommitAsync(context.RequestAborted);

            if (failure is not null)
            {
                _diagnosticEvents.RequestError(context, failure);
                context.Result = ErrorHelper.AuthorizationFailed();
                return;
            }

            var decisions = evaluation.Decisions;

            if (decisions is not null
                && decisions.TryGetRejection(_options.RejectRequestOn, out var denial))
            {
                await RejectAsync(context, denial);
                return;
            }

            if (decisions is not null)
            {
                decisionSets ??= new PolicyDecisions?[variableSets.Length];
                decisionSets[i] = decisions;
            }

            if (isSubscription
                && decisions is not null
                && decisions.TryGetDenial(plan.Operation.RootSelectionSet, out var rootDenial))
            {
                await RejectAsync(context, rootDenial);
                return;
            }

            if (isSubscription)
            {
                TrackSubscription(
                    context,
                    user,
                    plan,
                    policies,
                    scope,
                    variableSets[i],
                    evaluation.DeniedDescriptors);
            }

            if (policies.Variables.IsEmpty)
            {
                continue;
            }

            if (!isSubscription && variableSets[i] is VariableValueCollection values)
            {
                PolicyVariableValues.AddTo(values, policies.Variables, decisions, _booleanType);
            }
            else
            {
                updatedVariableSets ??= [.. variableSets];
                updatedVariableSets[i] = PolicyVariableValues.Create(
                    variableSets[i],
                    policies.Variables,
                    decisions,
                    _booleanType);
            }
        }

        if (updatedVariableSets is not null)
        {
            context.VariableValues = ImmutableCollectionsMarshal.AsImmutableArray(updatedVariableSets);
        }

        if (decisionSets is not null)
        {
            context.SetPolicyDecisions(ImmutableCollectionsMarshal.AsImmutableArray(decisionSets));
        }

        await next(context);
    }

    private async ValueTask RefuseExpiredAsync(
        RequestContext context,
        IAuditTrail trail,
        ClaimsPrincipal user,
        OperationPlan plan,
        OperationPolicies policies,
        ImmutableArray<IVariableValueCollection> variableSets)
    {
        for (var i = 0; i < variableSets.Length; i++)
        {
            var scope = trail.BeginRequest(
                new AuditScopeInfo(plan.Operation.Id, plan.Id, context.RequestIndex, i),
                user);

            _evaluator.RecordDenied(
                scope,
                plan,
                policies,
                variableSets[i],
                PolicyEvaluator.UnauthenticatedReason);
            await scope.CommitAsync(context.RequestAborted);
        }

        var challenge = await _schemeResolver.GetChallengeAsync(context.RequestAborted);
        var result = ErrorHelper.TokenExpired(challenge);

        _diagnosticEvents.RequestError(context, result.Errors[0]);
        context.Result = result;
    }

    private void TrackSubscription(
        RequestContext context,
        ClaimsPrincipal user,
        OperationPlan plan,
        OperationPolicies policies,
        IAuditScope scope,
        IVariableValueCollection variables,
        FrozenSet<PolicyDescriptor> deniedDescriptors)
    {
        if (!policies.HasReevaluatedPolicies && SubscriptionPolicies.GetExpiry(user) is null)
        {
            return;
        }

        context.Features.Set(
            new SubscriptionPolicies(
                _evaluator,
                scope,
                plan,
                policies,
                variables,
                deniedDescriptors,
                _booleanType,
                _timeProvider));
    }

    private async ValueTask RejectAsync(RequestContext context, SelectionDenial denial)
    {
        var challenge = denial.Kind is AuthorizationDenialKind.Unauthenticated
            ? await _schemeResolver.GetChallengeAsync(context.RequestAborted)
            : null;

        var result = ErrorHelper.AuthorizationRejected(denial, _options.EnableAttribution, challenge);

        _diagnosticEvents.RequestError(context, result.Errors[0]);
        context.Result = result;
    }

    internal static ClaimsPrincipal GetUser(RequestContext context)
        => context.ContextData.TryGetValue(nameof(ClaimsPrincipal), out var value)
            && value is ClaimsPrincipal user
                ? user
                : new ClaimsPrincipal(new ClaimsIdentity());

    public static RequestMiddlewareConfiguration Create()
        => new RequestMiddlewareConfiguration(
            (fc, next) =>
            {
                var schema = fc.SchemaServices.GetRequiredService<FusionSchemaDefinition>();
                var options = fc.SchemaServices.GetRequiredService<FusionAuthorizationOptions>();
                var schemeResolver = fc.SchemaServices.GetRequiredService<AuthenticationSchemeResolver>();
                var diagnosticEvents = fc.SchemaServices.GetRequiredService<IFusionExecutionDiagnosticEvents>();
                var auditProvider = fc.SchemaServices.GetRequiredService<IAuditProvider>();
                var booleanType = GetBooleanType(schema);
                var timeProvider = fc.SchemaServices.GetService<TimeProvider>() ?? TimeProvider.System;
                var middleware = new OperationPolicyMiddleware(
                    new PolicyEvaluator(options, diagnosticEvents),
                    auditProvider,
                    options,
                    schemeResolver,
                    diagnosticEvents,
                    booleanType,
                    timeProvider);
                return requestContext => middleware.InvokeAsync(requestContext, next);
            },
            Key);

    private static IInputType GetBooleanType(FusionSchemaDefinition schema)
    {
        var typeNode = new NonNullTypeNode(new NamedTypeNode(SpecScalarNames.Boolean.Name));

        if (schema.Types.TryGetType(typeNode, out IInputType? type))
        {
            return type;
        }

        throw ThrowHelper.OperationPolicyRequiresBooleanType();
    }
}
