using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Results;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Types;
using Microsoft.Extensions.Primitives;

namespace HotChocolate.Fusion.Execution;

public sealed partial class OperationPlanContext
{
    private CancellationTokenSource _engineCancellationSource = new();
    private ConditionFlags _activeDeliveryGroups;
    private ConditionFlags _runningIncrementalPlans;
    private readonly IRequestHeaderProvider? _requestHeaderProvider;
    private ImmutableArray<string> _propagatedHeaderNames = [];
    private StringValues[] _propagatedHeaderValues = [];
    private bool _hasPropagatedHeaders;

    internal OperationPlanContext(
        INodeIdParser nodeIdParser,
        IFusionExecutionDiagnosticEvents diagnosticEvents,
        IErrorHandler errorHandler,
        IRequestHeaderProvider? requestHeaderProvider)
    {
        _nodeIdParser = nodeIdParser;
        _diagnosticEvents = diagnosticEvents;
        _errorHandler = errorHandler;
        _requestHeaderProvider = requestHeaderProvider;
        _resultStore = new FetchResultStore();
        _executionState = new ExecutionState();
    }

    internal void Initialize(
        RequestContext requestContext,
        IVariableValueCollection variables,
        int variableIndex,
        PolicyEvaluationResult policyResult,
        IOperationPlan operationPlan,
        CancellationTokenSource cancellationTokenSource,
        MemoryArena? memory = null)
    {
        _activeNodeSlotCount = 0;
        _usesDynamicSchemaNames = true;
        _usesBatchNodes = true;

        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(operationPlan);

        _disposed = 0;
        RequestContext = requestContext;

        var options = requestContext.Schema.GetOptions();
        CapturePropagatedHeaders(requestContext, options.PropagatedHeaders);

        _memory = memory
            ?? requestContext.Memory
            ?? throw new InvalidOperationException(
                "The operation plan context requires a memory arena.");
        _memorySource.Set(_memory);
        _currentMemorySource = _memorySource;

        Variables = variables;
        _variableIndex = variableIndex;
        OperationPlan = operationPlan;

        PolicyEvaluationResult = policyResult;

        switch (operationPlan)
        {
            case OperationPlan plan:
                _usesDynamicSchemaNames = plan.UsesDynamicSchemaNames;
                _usesBatchNodes = plan.UsesBatchNodes;
                _entityReuseCandidateMap = options.EnableEntityReuse
                    ? plan.EntityReuseCandidateMap
                    : null;
                break;

            case IncrementalPlan plan:
                _usesDynamicSchemaNames = plan.UsesDynamicSchemaNames;
                _usesBatchNodes = plan.UsesBatchNodes;
                _entityReuseCandidateMap = null;
                break;
        }

        IncludeConditionFlags = operationPlan.Operation.CreateIncludeConditionFlags(variables);
        DeferConditionFlags = operationPlan.Operation.CreateDeferConditionFlags(variables);
        _collectTelemetry = requestContext.CollectOperationPlanTelemetry();
        _clientScope ??= requestContext.CreateClientScope();
        _clientScopeCreatedAt = Stopwatch.GetTimestamp();

        if (_clientScope is DefaultSourceSchemaClientScope clientScope)
        {
            clientScope.BeginRequest(_clientScopeCreatedAt);
        }

        _resultStore.Initialize(
            Memory,
            (FusionSchemaDefinition)requestContext.Schema,
            _errorHandler,
            operationPlan.Operation,
            requestContext.ErrorHandlingMode(),
            IncludeConditionFlags,
            DeferConditionFlags,
            options.PathSegmentLocalPoolCapacity);

        _executionState.Initialize(_collectTelemetry, cancellationTokenSource);

        var maxNodeId = operationPlan.MaxNodeId;
        EnsureNodeArrayCapacity(maxNodeId);
        _activeNodeSlotCount = maxNodeId + 1;
    }

    /// <summary>
    /// Gets the delivery groups that are active for the request, indexed by
    /// <see cref="DeliveryGroup.Id"/>.
    /// </summary>
    internal ConditionFlags ActiveDeliveryGroups => _activeDeliveryGroups;

    /// <summary>
    /// Gets the incremental plans that run for the request, indexed like
    /// <see cref="OperationPlan.IncrementalPlans"/>.
    /// </summary>
    internal ConditionFlags RunningIncrementalPlans => _runningIncrementalPlans;

    /// <summary>
    /// Computes the active delivery groups and the running incremental plans of
    /// <paramref name="plan"/> for <paramref name="variables"/>.
    /// </summary>
    internal void ActivateIncrementalPlans(OperationPlan plan, IVariableValueCollection variables)
    {
        _activeDeliveryGroups = DeliveryGroup.GetActive(plan.DeliveryGroups, variables);
        _runningIncrementalPlans = IncrementalPlan.GetRunningPlans(plan.IncrementalPlans, _activeDeliveryGroups);
    }

    /// <summary>
    /// Arms the pooled engine cancellation source against the request token and returns it for
    /// this operation. The engine source halts the execution engine without cancelling the request
    /// pipeline. The returned registration links the request token into the engine source so that
    /// client-abort and server-shutdown still propagate, and it must be disposed before the source
    /// is returned for reuse.
    /// </summary>
    internal (CancellationTokenSource Source, CancellationTokenRegistration Registration) RentEngineCancellation(
        CancellationToken cancellationToken)
    {
        var registration = cancellationToken.UnsafeRegister(
            static state => Unsafe.As<CancellationTokenSource>(state)!.Cancel(),
            _engineCancellationSource);
        return (_engineCancellationSource, registration);
    }

    /// <summary>
    /// Returns the engine cancellation source for reuse. If it was cancelled and cannot be reset,
    /// it is disposed and replaced with a fresh source. The caller must dispose the registration
    /// from <see cref="RentEngineCancellation"/> before calling this.
    /// </summary>
    internal void ReturnEngineCancellation()
    {
        if (!_engineCancellationSource.TryReset())
        {
            _engineCancellationSource.Dispose();
            _engineCancellationSource = new CancellationTokenSource();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the request has a header
    /// that the gateway copies to source schema requests.
    /// </summary>
    internal bool HasPropagatedHeaders => _hasPropagatedHeaders;

    /// <summary>
    /// Adds the propagated headers of the request to a source schema request.
    /// A header that the source schema request already has is kept, and so is a header
    /// that <paramref name="defaultRequestHeaders"/> has.
    /// </summary>
    /// <param name="message">The source schema request.</param>
    /// <param name="defaultRequestHeaders">
    /// The headers that the HTTP client adds to a request that does not have them,
    /// or <c>null</c> when it adds none.
    /// </param>
    internal void AddPropagatedHeaders(HttpRequestMessage message, HttpRequestHeaders? defaultRequestHeaders)
    {
        if (!_hasPropagatedHeaders)
        {
            return;
        }

        var names = _propagatedHeaderNames;
        var values = _propagatedHeaderValues;
        var headers = message.Headers;

        for (var i = 0; i < values.Length; i++)
        {
            var name = names[i];
            var value = values[i];

            if (value.Count == 0
                || headers.NonValidated.Contains(name)
                || defaultRequestHeaders?.NonValidated.Contains(name) == true)
            {
                continue;
            }

            if (value.Count == 1)
            {
                headers.TryAddWithoutValidation(name, value[0]);
            }
            else
            {
                headers.TryAddWithoutValidation(name, (IEnumerable<string?>)value);
            }
        }
    }

    // The values are read once, while the pipeline of the request runs.
    private void CapturePropagatedHeaders(RequestContext requestContext, ImmutableArray<string> names)
    {
        if (names.Length == 0 || _requestHeaderProvider is null)
        {
            return;
        }

        if (_propagatedHeaderValues.Length != names.Length)
        {
            _propagatedHeaderValues = new StringValues[names.Length];
        }

        var values = _propagatedHeaderValues;
        var hasValues = false;

        for (var i = 0; i < names.Length; i++)
        {
            var value = _requestHeaderProvider.GetHeader(requestContext, names[i]);
            values[i] = value;
            hasValues |= value.Count > 0;
        }

        _propagatedHeaderNames = names;
        _hasPropagatedHeaders = hasValues;
    }

    /// <summary>
    /// Cleans the context for return to the pool.
    /// Releases per-request state while retaining reusable buffers.
    /// </summary>
    internal void Clean()
    {
        var activeNodeSlotCount = _activeNodeSlotCount;

        if (activeNodeSlotCount > 0)
        {
            DisposeNodeState(activeNodeSlotCount);
            Array.Clear(_nodesToComplete, 0, activeNodeSlotCount);

            if (_usesDynamicSchemaNames)
            {
                Array.Clear(_schemaNames, 0, activeNodeSlotCount);
            }

            if (_usesBatchNodes)
            {
                Array.Clear(_skippedDefinitions, 0, activeNodeSlotCount);
                Array.Clear(_batchRequestErrors, 0, activeNodeSlotCount);
            }

            if (_collectTelemetry)
            {
                Array.Clear(_variableValueSets, 0, activeNodeSlotCount);
                Array.Clear(_transportUris, 0, activeNodeSlotCount);
                Array.Clear(_transportContentTypes, 0, activeNodeSlotCount);
            }
        }

        _activeNodeSlotCount = 0;
        _usesDynamicSchemaNames = true;
        _usesBatchNodes = true;
        HasExecutionDeadline = false;

        _resultStore.Clean(256, 256);
        _executionState.Clean();

        RequestContext = default!;

        if (_hasPropagatedHeaders)
        {
            Array.Clear(_propagatedHeaderValues);
            _hasPropagatedHeaders = false;
        }

        _memory = null;
        _memorySource.Clear();
        _currentMemorySource = null!;
        Variables = default!;
        OperationPlan = default!;
        PolicyEvaluationResult = PolicyEvaluationResult.Empty;
        _entityReuseCandidateMap = null;
        IncludeConditionFlags = default;
        DeferConditionFlags = default;
        _activeDeliveryGroups = default;
        _runningIncrementalPlans = default;
        // if a custom scope is used we cannot reuse it and have to null it.
        if (_clientScope is not DefaultSourceSchemaClientScope)
        {
            _clientScope = default!;
        }
        _requirementValues = default;
        _requirementKeys = null;
        Traces =
#if NET10_0_OR_GREATER
            [];
#else
            ImmutableDictionary<int, ExecutionNodeTrace>.Empty;
#endif
        _traceId = null;
        _start = 0;
        _clientScopeCreatedAt = 0;
    }

    /// <summary>
    /// Permanently destroys the context and its owned resources.
    /// Called when the pool drops a context (pool full) or during pool disposal.
    /// </summary>
    internal void Destroy()
    {
        _resultStore.Dispose();
        _executionState.Destroy();
        _engineCancellationSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // If Initialize fails before creating a scope, _clientScope can be null.
        if (_clientScope is DefaultSourceSchemaClientScope reusableClientScope)
        {
            await reusableClientScope.ResetAsync();
        }
        else if (_clientScope is not null)
        {
            await _clientScope.DisposeAsync();
        }

        var pool = _pool;
        _pool = null;
        pool?.Return(this);
    }

    private void EnsureNodeArrayCapacity(int maxNodeId)
    {
        var nodeSlotCount = maxNodeId + 1;
        _dependentBitsetWordCount = (maxNodeId >> 6) + 1;

        if (nodeSlotCount > _nodeSlotCapacity)
        {
            _nodesToComplete = new NodeCompletionSet?[nodeSlotCount];
            _schemaNames = new string?[nodeSlotCount];
            _skippedDefinitions = new List<IOperationPlanNode>?[nodeSlotCount];
            _batchRequestErrors = new Dictionary<int, Exception>?[nodeSlotCount];
            _variableValueSets = new ImmutableArray<VariableValues>[nodeSlotCount];
            _transportUris = new Uri?[nodeSlotCount];
            _transportContentTypes = new string?[nodeSlotCount];
            _nodeSlotCapacity = nodeSlotCount;
        }
    }
}
