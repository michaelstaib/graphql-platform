using System.Runtime.InteropServices;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization;
using HotChocolate.Fusion.Authorization.Audit;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution.Pipeline;

internal sealed class OperationAuthorizationMiddleware
{
    private const string Key = "FusionOperationAuthorizationMiddleware";

    private readonly AuthorizationEvaluator _evaluator;
    private readonly IAuditProvider _auditProvider;
    private readonly FusionAuthorizationOptions _options;
    private readonly AuthenticationSchemeResolver _schemeResolver;
    private readonly IFusionExecutionDiagnosticEvents _diagnosticEvents;
    private readonly IInputType _variableType;
    private readonly TimeProvider _timeProvider;

    private OperationAuthorizationMiddleware(
        AuthorizationEvaluator evaluator,
        IAuditProvider auditProvider,
        FusionAuthorizationOptions options,
        AuthenticationSchemeResolver schemeResolver,
        IFusionExecutionDiagnosticEvents diagnosticEvents,
        IInputType variableType,
        TimeProvider timeProvider)
    {
        _evaluator = evaluator;
        _auditProvider = auditProvider;
        _options = options;
        _schemeResolver = schemeResolver;
        _diagnosticEvents = diagnosticEvents;
        _variableType = variableType;
        _timeProvider = timeProvider;
    }

    public ValueTask InvokeAsync(RequestContext context, RequestDelegate next)
    {
        var plan = context.GetOperationPlan() ?? throw ThrowHelper.OperationAuthorizationRequiresPlan();

        return plan.Authorization is { } authorization
            ? InvokeProtectedAsync(context, plan, authorization, next)
            : next(context);
    }

    private async ValueTask InvokeProtectedAsync(
        RequestContext context,
        OperationPlan plan,
        OperationAuthorization authorization,
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
        var updatedVariableSets = new IVariableValueCollection[variableSets.Length];
        var isSubscription = plan.Operation.Definition.Operation is OperationType.Subscription;

        for (var i = 0; i < variableSets.Length; i++)
        {
            var scope = trail.BeginRequest(
                new AuditScopeInfo(plan.Operation.Id, plan.Id, context.RequestIndex, i),
                user);

            AuthorizationEvaluation evaluation;

            try
            {
                evaluation = await _evaluator.EvaluateAsync(
                    context,
                    user,
                    plan,
                    authorization,
                    variableSets[i],
                    scope,
                    i,
                    null,
                    context.RequestAborted);
            }
            catch (Exception ex) when (AuthorizationEvaluator.IsFault(ex, context.RequestAborted))
            {
                await _evaluator.CommitFaultedScopeAsync(context, scope, ex);

                if (ex is OperationCanceledException)
                {
                    throw ThrowHelper.OperationAuthorizationFaulted(ex);
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

            if (isSubscription
                && decisions is not null
                && decisions.TryGetDenial(plan.Operation.RootSelectionSet, out var rootDenial))
            {
                await RejectAsync(context, rootDenial);
                return;
            }

            var variableValues = AuthorizationVariableValues.Create(
                variableSets[i],
                authorization.Variables,
                decisions,
                _variableType);

            updatedVariableSets[i] = variableValues;

            if (isSubscription)
            {
                TrackSubscription(
                    context,
                    user,
                    plan,
                    authorization,
                    scope,
                    variableSets[i],
                    variableValues,
                    evaluation.DeniedDescriptors);
            }
        }

        context.VariableValues = ImmutableCollectionsMarshal.AsImmutableArray(updatedVariableSets);

        await next(context);
    }

    private void TrackSubscription(
        RequestContext context,
        ClaimsPrincipal user,
        OperationPlan plan,
        OperationAuthorization authorization,
        IAuditScope scope,
        IVariableValueCollection variables,
        VariableValueCollection variableValues,
        IReadOnlySet<PolicyDescriptor> deniedDescriptors)
    {
        if (!authorization.HasReevaluatedPolicies && SubscriptionAuthorization.GetExpiry(user) is null)
        {
            return;
        }

        context.Features.Set(
            new SubscriptionAuthorization(
                _evaluator,
                scope,
                plan,
                authorization,
                variables,
                variableValues,
                deniedDescriptors,
                _variableType,
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
                var variableType = GetVariableType(schema);
                var timeProvider = fc.SchemaServices.GetService<TimeProvider>() ?? TimeProvider.System;
                var middleware = new OperationAuthorizationMiddleware(
                    new AuthorizationEvaluator(options, diagnosticEvents),
                    auditProvider,
                    options,
                    schemeResolver,
                    diagnosticEvents,
                    variableType,
                    timeProvider);
                return requestContext => middleware.InvokeAsync(requestContext, next);
            },
            Key);

    private static IInputType GetVariableType(FusionSchemaDefinition schema)
    {
        var typeNode = new NonNullTypeNode(new NamedTypeNode(SpecScalarNames.Boolean.Name));

        if (schema.Types.TryGetType(typeNode, out IInputType? type))
        {
            return type;
        }

        throw ThrowHelper.OperationAuthorizationRequiresBooleanType();
    }
}
