using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution.Pipeline;

internal sealed class OperationPolicyMiddleware
{
    private const string Key = "FusionOperationPolicyMiddleware";

    private readonly PolicyEvaluator _evaluator;
    private readonly FusionAuthorizationOptions _options;
    private readonly AuthenticationSchemeResolver _schemeResolver;
    private readonly IFusionExecutionDiagnosticEvents _diagnosticEvents;
    private readonly IInputType _booleanType;

    private OperationPolicyMiddleware(
        PolicyEvaluator evaluator,
        FusionAuthorizationOptions options,
        AuthenticationSchemeResolver schemeResolver,
        IFusionExecutionDiagnosticEvents diagnosticEvents,
        IInputType booleanType)
    {
        _evaluator = evaluator;
        _options = options;
        _schemeResolver = schemeResolver;
        _diagnosticEvents = diagnosticEvents;
        _booleanType = booleanType;
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
        OperationPolicies policies,
        RequestDelegate next)
    {
        var variableSets = context.VariableValues;

        if (variableSets.IsDefaultOrEmpty)
        {
            await next(context);
            return;
        }

        var user = GetUser(context);
        var updatedVariableSets = new IVariableValueCollection[variableSets.Length];

        for (var i = 0; i < variableSets.Length; i++)
        {
            var evaluation = await _evaluator.EvaluateAsync(
                context,
                user,
                plan,
                policies,
                variableSets[i],
                i,
                context.RequestAborted);

            if (evaluation.Failure is { } failure)
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

            updatedVariableSets[i] = CreateVariableValues(variableSets[i], policies.Variables, decisions);
        }

        context.VariableValues = ImmutableCollectionsMarshal.AsImmutableArray(updatedVariableSets);

        await next(context);
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

    private IVariableValueCollection CreateVariableValues(
        IVariableValueCollection variableValues,
        ImmutableArray<PolicyVariable> variables,
        PolicyDecisions? decisions)
    {
        var values = new Dictionary<string, VariableValue>();

        foreach (var value in variableValues)
        {
            values[value.Name] = value;
        }

        PolicyVariableValues.AddTo(values, variables, decisions, _booleanType);

        return new VariableValueCollection(values, decisions);
    }

    private static ClaimsPrincipal GetUser(RequestContext context)
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
                var booleanType = GetBooleanType(schema);
                var middleware = new OperationPolicyMiddleware(
                    new PolicyEvaluator(options),
                    options,
                    schemeResolver,
                    diagnosticEvents,
                    booleanType);
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
