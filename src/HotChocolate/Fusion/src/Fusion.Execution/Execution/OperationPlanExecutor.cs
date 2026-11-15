using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using HotChocolate.Buffers;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Results;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Text.Json;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Execution;

internal static partial class OperationPlanExecutor
{
    public static async Task<IExecutionResult> ExecuteAsync(
        RequestContext requestContext,
        IVariableValueCollection variables,
        int variableIndex,
        PolicyEvaluationResult policyResult,
        OperationPlan operationPlan,
        CancellationToken cancellationToken)
    {
        if (!operationPlan.DeliveryGroups.IsDefaultOrEmpty
            && operationPlan.IncrementalPlans.IsDefaultOrEmpty
            && variableIndex < 0
            && operationPlan.Operation.Definition.Operation is OperationType.Query or OperationType.Mutation)
        {
            return await ExecuteWithSinglePlanDeferAsync(
                requestContext,
                variables,
                policyResult,
                operationPlan,
                cancellationToken);
        }

        await using var context = requestContext.Schema.Services.GetRequiredService<OperationPlanContextPool>().Rent();

        // We reuse a pooled CancellationTokenSource that can halt the execution engine without
        // cancelling the entire request pipeline. The request token is linked in so that
        // client-abort / server-shutdown still propagates.
        var (executionCts, cancellationRegistration) = context.RentEngineCancellation(cancellationToken);

        try
        {
            context.Initialize(
                requestContext,
                variables,
                variableIndex,
                policyResult,
                operationPlan,
                executionCts);

            // The engine token is linked to the request token, so it shares the request deadline.
            context.HasExecutionDeadline =
                requestContext.Features.Get<ExecutionDeadline>()?.Covers(cancellationToken) is true;

            context.Begin();

            switch (operationPlan.Operation.Definition.Operation)
            {
                case OperationType.Query:
                    await ExecuteQueryAsync(context, operationPlan, executionCts.Token);
                    break;

                case OperationType.Mutation:
                    await ExecuteMutationAsync(context, operationPlan, executionCts.Token);
                    break;

                default:
                    throw new InvalidOperationException("Only queries and mutations can be executed.");
            }

            // If the original CancellationToken of the request was cancelled,
            // the Execution nodes and the PlanExecutor should have been gracefully cancelled,
            // so we throw here to properly cancel the request execution.
            cancellationToken.ThrowIfCancellationRequested();

            return context.Complete();
        }
        finally
        {
            // Dispose the parent-token registration BEFORE returning the source for reuse so a
            // stale registration can never fire into a reset source. DisposeAsync waits for an
            // in-flight cancel callback to finish.
            await cancellationRegistration.DisposeAsync();
            context.ReturnEngineCancellation();
        }
    }

    public static async Task<IExecutionResult> ExecuteWithDeferAsync(
        RequestContext requestContext,
        IVariableValueCollection variables,
        PolicyEvaluationResult policyResult,
        OperationPlan operationPlan,
        CancellationToken cancellationToken)
    {
        EnsureDeferIfVariablesAreNotNull(variables, operationPlan);

        // Execute the main (non-deferred) plan nodes first.
        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        OperationPlanContext? rootContext = null;

        try
        {
            rootContext = requestContext.Schema.Services.GetRequiredService<OperationPlanContextPool>().Rent();
            rootContext.Initialize(
                requestContext,
                variables,
                variableIndex: -1,
                policyResult,
                operationPlan,
                executionCts);

            rootContext.Begin();

            switch (operationPlan.Operation.Definition.Operation)
            {
                case OperationType.Query:
                    await ExecuteQueryAsync(rootContext, operationPlan, executionCts.Token);
                    break;

                case OperationType.Mutation:
                    await ExecuteMutationAsync(rootContext, operationPlan, executionCts.Token);
                    break;

                default:
                    throw new InvalidOperationException("Only queries and mutations can use @defer.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Complete the initial result while retaining data needed by active
            // incremental plans.
            var initialResult = rootContext.Complete(retainMemoryForDefer: true);

            // Compute the active delivery groups (one per @defer occurrence whose
            // @defer(if:) evaluates to true). A plan runs when one of its delivery groups is
            // active or has an active enclosing group, the rule the policy evaluator shares.
            rootContext.ActivateIncrementalPlans(operationPlan, variables);
            var activeDeliveryGroups = rootContext.ActiveDeliveryGroups;
            var activeDeliveryGroupIds = new HashSet<int>();
            foreach (var deliveryGroup in operationPlan.DeliveryGroups)
            {
                if (activeDeliveryGroups.IsSet(deliveryGroup.Id))
                {
                    activeDeliveryGroupIds.Add(deliveryGroup.Id);
                }
            }

            var deliveryGroupsByPlan = ResolveDeliveryGroupsByPlan(operationPlan, activeDeliveryGroupIds);

            // Mark top-level active delivery groups as pending on the initial
            // result. A group whose enclosing groups are all disabled is top-level.
            // Nested delivery groups are marked pending after their enclosing
            // delivery group completes.
            var pendingResults = ImmutableList.CreateBuilder<PendingResult>();
            foreach (var deliveryGroup in operationPlan.DeliveryGroups)
            {
                if (!activeDeliveryGroupIds.Contains(deliveryGroup.Id))
                {
                    continue;
                }

                if (GetEnabledDeliveryGroup(deliveryGroup.Parent, activeDeliveryGroupIds) is not null)
                {
                    continue;
                }

                pendingResults.Add(new PendingResult(
                    deliveryGroup.Id,
                    operationPlan.GetDeliveryPath(deliveryGroup.Id).PendingPath,
                    deliveryGroup.Label));
            }

            initialResult.HasNext = pendingResults.Count > 0;
            initialResult.Pending = pendingResults.ToImmutable();

            if (pendingResults.Count == 0)
            {
                // No active top-level delivery groups. Transfer retained
                // result resources to the initial result.
                rootContext.TransferRetainedMemoryTo(initialResult);
                executionCts.Dispose();
                await rootContext.DisposeAsync();
                return initialResult;
            }

            // Capture the single request arena now. The request executor detaches it from the
            // request context once this method returns the stream, so every incremental plan reuses
            // the captured arena instead of minting its own, and the stream seals it once when read
            // to completion.
            var stream = new ResponseStream(
                () => CreateIncrementalStream(
                    requestContext,
                    variables,
                    policyResult,
                    operationPlan,
                    initialResult,
                    activeDeliveryGroupIds,
                    deliveryGroupsByPlan,
                    rootContext,
                    cancellationToken),
                ExecutionResultKind.DeferredResult);

            stream.RegisterForCleanup(rootContext);
            stream.RegisterForCleanup(executionCts);
            return stream;
        }
        catch (Exception)
        {
            executionCts.Dispose();

            if (rootContext is not null)
            {
                await rootContext.DisposeAsync();
            }

            throw;
        }
    }

    /// <summary>
    /// Runs a plan whose steps carry delivery groups on one context and one result document. The steps
    /// without groups run first and form the initial payload, the marked steps run after it.
    /// </summary>
    private static async Task<IExecutionResult> ExecuteWithSinglePlanDeferAsync(
        RequestContext requestContext,
        IVariableValueCollection variables,
        PolicyEvaluationResult policyResult,
        OperationPlan operationPlan,
        CancellationToken cancellationToken)
    {
        EnsureDeferIfVariablesAreNotNull(variables, operationPlan);

        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        OperationPlanContext? context = null;

        try
        {
            context = requestContext.Schema.Services.GetRequiredService<OperationPlanContextPool>().Rent();
            context.Initialize(
                requestContext,
                variables,
                variableIndex: -1,
                policyResult,
                operationPlan,
                executionCts);

            context.Begin();

            var state = context.EnableSinglePlanDelivery(operationPlan);
            var executionState = context.ExecutionState;
            var token = executionCts.Token;

            await using (var registration = token.Register(
                static signal => Unsafe.As<AsyncAutoResetEvent>(signal)!.TryResetToIdle(),
                executionState.Signal))
            {
                executionState.FillBacklog(operationPlan);

                foreach (var node in state.MarkedNodes)
                {
                    executionState.HoldNode(node);
                }

                var serial = operationPlan.Operation.Definition.Operation is OperationType.Mutation;

                foreach (var root in operationPlan.RootNodes)
                {
                    if (SinglePlanDeliveryState.GetDeliveryGroups(root).IsDefaultOrEmpty)
                    {
                        executionState.StartNode(context, root, token);

                        if (serial)
                        {
                            await RunSinglePlanInitialPhaseAsync(context, operationPlan, state, token);

                            if (token.IsCancellationRequested)
                            {
                                break;
                            }
                        }
                    }
                }

                await RunSinglePlanInitialPhaseAsync(context, operationPlan, state, token);

                await DrainActiveNodesAsync(operationPlan, executionState);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var halted = executionCts.IsCancellationRequested;
            var initialResult = context.Complete(retainMemoryForDefer: true);

            context.ActivateIncrementalPlans(operationPlan, variables);

            if (!halted)
            {
                state.ComputeNeeded();
            }

            var pendingResults = ImmutableList.CreateBuilder<PendingResult>();

            foreach (var deliveryGroup in operationPlan.DeliveryGroups)
            {
                if (state.Honored[deliveryGroup.Id] && state.GetHonoredParent(deliveryGroup) is null)
                {
                    state.Announced[deliveryGroup.Id] = true;
                    pendingResults.Add(CreatePendingResult(operationPlan, deliveryGroup));
                }
            }

            initialResult.HasNext = pendingResults.Count > 0;
            initialResult.Pending = pendingResults.ToImmutable();
            state.PublishedErrorCount = context.ErrorCount;

            if (pendingResults.Count == 0)
            {
                context.TransferRetainedMemoryTo(initialResult);
                executionCts.Dispose();
                await context.DisposeAsync();
                return initialResult;
            }

            var requestArena = context.Memory;
            var stream = new ResponseStream(
                () => CreateSinglePlanStream(
                    context,
                    operationPlan,
                    state,
                    initialResult,
                    requestArena,
                    executionCts,
                    cancellationToken,
                    token),
                ExecutionResultKind.DeferredResult);

            stream.RegisterForCleanup(context);
            stream.RegisterForCleanup(executionCts);
            return stream;
        }
        catch (Exception)
        {
            executionCts.Dispose();

            if (context is not null)
            {
                await context.DisposeAsync();
            }

            throw;
        }
    }

    private static async Task RunSinglePlanInitialPhaseAsync(
        OperationPlanContext context,
        OperationPlan operationPlan,
        SinglePlanDeliveryState state,
        CancellationToken token)
    {
        var executionState = context.ExecutionState;

        while (!token.IsCancellationRequested && executionState.HasActiveNodes())
        {
            PumpSinglePlan(context, operationPlan, state, token);

            if (token.IsCancellationRequested || !executionState.HasActiveNodes())
            {
                break;
            }

            await executionState.Signal;
        }
    }

    private static PendingResult CreatePendingResult(OperationPlan operationPlan, DeliveryGroup deliveryGroup)
        => new(
            deliveryGroup.Id,
            operationPlan.GetDeliveryPath(deliveryGroup.Id).PendingPath,
            deliveryGroup.Label);

    /// <summary>
    /// Applies the merges and the completed steps that are waiting and starts the steps that became ready.
    /// </summary>
    private static void PumpSinglePlan(
        OperationPlanContext context,
        OperationPlan plan,
        SinglePlanDeliveryState state,
        CancellationToken cancellationToken)
    {
        var executionState = context.ExecutionState;

        while (executionState.TryDequeuePendingMerge(out var merge))
        {
            ApplySinglePlanMerge(context, executionState, merge);
        }

        context.ApplyDeliveryErrors(state);

        while (executionState.TryDequeueCompletedResult(out var result))
        {
            while (executionState.TryDequeuePendingMerge(out var merge))
            {
                ApplySinglePlanMerge(context, executionState, merge);
            }

            context.ApplyDeliveryErrors(state);

            result = executionState.ApplyPendingMergeFailure(result);
            var node = plan.GetNodeById(result.Id);
            executionState.CompleteNode(plan, node, result);
            state.Finished[result.Id] = true;
            state.Failed[result.Id] = result.Status is ExecutionStatus.Failed;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            executionState.EnqueueNextNodes(context, cancellationToken);
        }
    }

    private static void ApplySinglePlanMerge(
        OperationPlanContext context,
        ExecutionState executionState,
        PendingMerge merge)
    {
        var groups = SinglePlanDeliveryState.GetDeliveryGroups(merge.Node);

        if (groups.IsDefaultOrEmpty)
        {
            executionState.ApplyMerge(context, merge);
            return;
        }

        context.SetWriteFilter(groups);

        try
        {
            executionState.ApplyMerge(context, merge);
        }
        finally
        {
            context.ClearWriteFilter();
        }
    }

    private static async IAsyncEnumerable<OperationResult> CreateSinglePlanStream(
        OperationPlanContext context,
        OperationPlan plan,
        SinglePlanDeliveryState state,
        OperationResult initialResult,
        MemoryArena requestArena,
        CancellationTokenSource executionCts,
        CancellationToken requestCancellationToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completed = false;

        try
        {
            yield return initialResult;

            // The consumer asked for the next payload, so the initial payload is written out.
            var executionState = context.ExecutionState;

            await using var registration = cancellationToken.Register(
                static signal => Unsafe.As<AsyncAutoResetEvent>(signal)!.TryResetToIdle(),
                executionState.Signal);

            state.QueuesErrors = true;

            foreach (var node in state.MarkedNodes)
            {
                if (state.Needed[node.Id])
                {
                    executionState.ReleaseNode(node);
                }
                else
                {
                    executionState.SkipNode(plan, node);
                }
            }

            var hasNext = true;
            IError? fault = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                OperationResult? payload;

                try
                {
                    PumpSinglePlan(context, plan, state, cancellationToken);
                    payload = CreateSinglePlanPayload(context, plan, state, out hasNext);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    fault = ErrorHelper.UnexpectedExecutionError(ex);
                    hasNext = true;
                    await executionCts.CancelAsync();
                    break;
                }

                if (payload is not null)
                {
                    yield return payload;

                    if (!hasNext)
                    {
                        break;
                    }

                    continue;
                }

                if (!hasNext)
                {
                    break;
                }

                if (!executionState.HasActiveNodes())
                {
                    // No step runs any more, so the remaining groups can never be delivered.
                    break;
                }

                await executionState.Signal;
            }

            if (hasNext && !requestCancellationToken.IsCancellationRequested)
            {
                await DrainActiveNodesAsync(plan, executionState);
                yield return CreateSinglePlanHaltedPayload(context, plan, state, fault);
            }

            await DrainActiveNodesAsync(plan, executionState);
            requestArena.Seal();
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                // The consumer stopped reading, so the steps that still run are cancelled.
                await executionCts.CancelAsync();
                await DrainActiveNodesAsync(plan, context.ExecutionState);
            }
        }
    }

    private static OperationResult CreateSinglePlanHaltedPayload(
        OperationPlanContext context,
        OperationPlan plan,
        SinglePlanDeliveryState state,
        IError? fault)
    {
        var completed = ImmutableList.CreateBuilder<CompletedResult>();
        var errors = context.GetErrorsAfter(state.PublishedErrorCount);

        if (fault is not null)
        {
            errors = errors.Add(fault);
        }

        foreach (var group in plan.DeliveryGroups)
        {
            if (state.Announced[group.Id] && !state.Completed[group.Id])
            {
                state.Completed[group.Id] = true;
                completed.Add(new CompletedResult(group.Id, errors.Count > 0 ? errors : null));
                errors = [];
            }
        }

        state.PublishedErrorCount = context.ErrorCount;

        return CreateSinglePlanPayloadResult([], [], completed.ToImmutable(), hasNext: false);
    }

    private static OperationResult CreateSinglePlanPayloadResult(
        ImmutableList<PendingResult> pending,
        ImmutableList<IIncrementalResult> incremental,
        ImmutableList<CompletedResult> completed,
        bool hasNext)
        => new(new IncrementalDataFeature
        {
            Pending = pending,
            Incremental = incremental,
            Completed = completed,
            HasNext = hasNext
        });

    private static OperationResult? CreateSinglePlanPayload(
        OperationPlanContext context,
        OperationPlan plan,
        SinglePlanDeliveryState state,
        out bool hasNext)
    {
        var executionState = context.ExecutionState;
        var pending = ImmutableList.CreateBuilder<PendingResult>();
        var incremental = ImmutableList.CreateBuilder<IIncrementalResult>();
        var completed = ImmutableList.CreateBuilder<CompletedResult>();
        var document = context.ResultDocument;
        var errors = context.GetErrorsAfter(state.PublishedErrorCount);
        var progressed = true;

        try
        {
            while (progressed)
            {
                progressed = false;

                foreach (var group in plan.DeliveryGroups)
                {
                    var id = group.Id;

                    if (!state.Announced[id] || state.Completed[id] || !state.IsSettled(id, executionState))
                    {
                        continue;
                    }

                    progressed = true;

                    var failed = false;

                    foreach (var node in state.NodesByGroup[id])
                    {
                        failed |= state.Failed[node.Id];
                    }

                    if (failed)
                    {
                        completed.Add(new CompletedResult(id, errors.Count > 0 ? errors : null));
                        errors = [];
                    }
                    else
                    {
                        var before = incremental.Count;
                        var data = document.Data;

                        CollectIncrementalResults(
                            document,
                            data,
                            group.Path ?? SelectionPath.Root,
                            segmentIndex: 0,
                            fieldIndex: 0,
                            plan.GetDeliveryPath(id).PendingFieldCount,
                            Path.Root,
                            id,
                            errors: null,
                            incremental,
                            claimRows: true);

                        if (errors.Count > 0 && incremental.Count > before)
                        {
                            var first = (IncrementalObjectResult)incremental[before];
                            incremental[before] = new IncrementalObjectResult(
                                first.Id,
                                errors,
                                first.SubPath,
                                first.Data);
                            errors = [];
                        }

                        completed.Add(new CompletedResult(id, errors.Count > 0 ? errors : null));
                        errors = [];
                    }

                    state.Completed[id] = true;

                    foreach (var child in plan.DeliveryGroups)
                    {
                        if (state.Honored[child.Id]
                            && !state.Announced[child.Id]
                            && state.GetHonoredParent(child) is { } parent
                            && parent.Id == id)
                        {
                            pending.Add(CreatePendingResult(plan, child));
                            state.Announced[child.Id] = true;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // The payload is never sent, so its groups are neither announced nor completed.
            foreach (var result in completed)
            {
                state.Completed[result.Id] = false;
            }

            foreach (var result in pending)
            {
                state.Announced[result.Id] = false;
            }

            throw;
        }

        hasNext = false;

        foreach (var group in plan.DeliveryGroups)
        {
            hasNext |= state.Honored[group.Id] && !state.Completed[group.Id];
        }

        if (pending.Count == 0 && incremental.Count == 0 && completed.Count == 0)
        {
            return null;
        }

        state.PublishedErrorCount = context.ErrorCount;

        return CreateSinglePlanPayloadResult(
            pending.ToImmutable(),
            incremental.ToImmutable(),
            completed.ToImmutable(),
            hasNext);
    }

    private static async IAsyncEnumerable<OperationResult> CreateIncrementalStream(
        RequestContext requestContext,
        IVariableValueCollection variables,
        PolicyEvaluationResult policyResult,
        OperationPlan operationPlan,
        OperationResult initialResult,
        HashSet<int> activeDeliveryGroupIds,
        Dictionary<IncrementalPlan, ImmutableArray<DeliveryGroup>> deliveryGroupsByPlan,
        OperationPlanContext rootContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Yield the initial result first.
        yield return initialResult;

        var requestArena = rootContext.Memory;
        var incrementalPlans = operationPlan.IncrementalPlans;

        // Per-delivery-group completion tracking. A delivery group is considered
        // complete when every incremental plan whose delivery groups contain it has
        // finished. The delivery groups of a plan are the enabled groups it delivers
        // with, so a disabled @defer(if: false) group does not block completion accounting
        // and its plan delivers with the nearest enabled enclosing group.
        var pendingCountByDeliveryGroup = new Dictionary<int, int>();
        foreach (var incrementalPlan in incrementalPlans)
        {
            foreach (var deliveryGroup in deliveryGroupsByPlan[incrementalPlan])
            {
                pendingCountByDeliveryGroup[deliveryGroup.Id] =
                    pendingCountByDeliveryGroup.GetValueOrDefault(deliveryGroup.Id) + 1;
            }
        }

        // An enabled delivery group that no incremental plan delivers with has no fields of its
        // own. It is announced like any other group and completes without running work when it is
        // top-level or encloses a delivery group of an incremental plan. Any other group is merged
        // into the plan of its enclosing group and is not announced.
        List<DeliveryGroup>? emptyDeliveryGroups = null;
        HashSet<int>? enclosingDeliveryGroupIds = null;
        foreach (var deliveryGroup in operationPlan.DeliveryGroups)
        {
            if (!activeDeliveryGroupIds.Contains(deliveryGroup.Id)
                || pendingCountByDeliveryGroup.ContainsKey(deliveryGroup.Id))
            {
                continue;
            }

            if (GetEnabledDeliveryGroup(deliveryGroup.Parent, activeDeliveryGroupIds) is not null)
            {
                if (enclosingDeliveryGroupIds is null)
                {
                    enclosingDeliveryGroupIds = [];

                    foreach (var incrementalPlan in incrementalPlans)
                    {
                        foreach (var planDeliveryGroup in deliveryGroupsByPlan[incrementalPlan])
                        {
                            for (var ancestor = planDeliveryGroup.Parent;
                                ancestor is not null;
                                ancestor = ancestor.Parent)
                            {
                                enclosingDeliveryGroupIds.Add(ancestor.Id);
                            }
                        }
                    }
                }

                if (!enclosingDeliveryGroupIds.Contains(deliveryGroup.Id))
                {
                    continue;
                }
            }

            pendingCountByDeliveryGroup.Add(deliveryGroup.Id, 1);
            (emptyDeliveryGroups ??= []).Add(deliveryGroup);
        }

        // Top-level incremental plans start immediately. Nested incremental
        // plans start when their enclosing delivery group has completed.
        //
        // Keep completed parent contexts available while nested incremental
        // plans are starting. Stored contexts are disposed when the iterator
        // finishes.
        var started = new HashSet<IncrementalPlan>();
        var incrementalPlanContexts = new Dictionary<IncrementalPlan, OperationPlanContext>();
        var channel = Channel.CreateUnbounded<IncrementalPlanResult>();
        var pendingIncrementalPlanCount = 0;

        try
        {
            foreach (var incrementalPlan in incrementalPlans)
            {
                var deliveryGroups = deliveryGroupsByPlan[incrementalPlan];

                if (deliveryGroups.IsEmpty
                    || GetEnabledDeliveryGroup(deliveryGroups[0].Parent, activeDeliveryGroupIds) is not null)
                {
                    continue;
                }

                started.Add(incrementalPlan);
                pendingIncrementalPlanCount++;
                BeginIncrementalPlan(
                    requestContext,
                    variables,
                    policyResult,
                    incrementalPlan,
                    deliveryGroups,
                    rootContext.GetResultStoreForChildDefer(),
                    channel.Writer,
                    requestArena,
                    cancellationToken);
            }

            // Track which delivery groups we have already announced as pending so
            // we do not re-announce nested groups multiple times when they belong
            // to more than one incremental plan.
            var announcedDeliveryGroupIds = new HashSet<int>();
            foreach (var deliveryGroup in operationPlan.DeliveryGroups)
            {
                if (activeDeliveryGroupIds.Contains(deliveryGroup.Id)
                    && GetEnabledDeliveryGroup(deliveryGroup.Parent, activeDeliveryGroupIds) is null)
                {
                    announcedDeliveryGroupIds.Add(deliveryGroup.Id);
                }
            }

            // The incremental plan context that nested incremental plans of an empty delivery group
            // resolve their requirements from.
            var emptyDeliveryGroupScopes = emptyDeliveryGroups is null
                ? null
                : new Dictionary<int, OperationPlanContext?>();

            if (emptyDeliveryGroups is not null)
            {
                foreach (var deliveryGroup in emptyDeliveryGroups)
                {
                    if (!announcedDeliveryGroupIds.Contains(deliveryGroup.Id))
                    {
                        continue;
                    }

                    pendingIncrementalPlanCount++;
                    channel.Writer.TryWrite(new IncrementalPlanResult(null, [deliveryGroup], null, null, null));
                }
            }

            // Delivery groups of failed incremental plans and the errors they complete with.
            Dictionary<int, IReadOnlyList<IError>>? failedDeliveryGroups = null;

            // Yield results as they complete.
            while (pendingIncrementalPlanCount > 0 && !cancellationToken.IsCancellationRequested)
            {
                var (incrementalPlan, deliveryGroups, incrementalPlanContext, result, error) =
                    await channel.Reader.ReadAsync(cancellationToken);
                pendingIncrementalPlanCount--;

                // Register the completed incremental plan's context so any nested
                // defer launched below can source its parent store from this
                // immediately enclosing incremental plan rather than the root plan.
                if (incrementalPlan is not null && incrementalPlanContext is not null)
                {
                    incrementalPlanContexts[incrementalPlan] = incrementalPlanContext;
                }

                var scopeContext = incrementalPlanContext;
                if (incrementalPlan is null)
                {
                    emptyDeliveryGroupScopes?.TryGetValue(deliveryGroups[0].Id, out scopeContext);
                }

                // Use the deepest delivery group path for this incremental
                // plan. Ties are broken by the delivery group this payload
                // completes, then by the smallest DeliveryGroup.Id.
                var bestDeliveryGroup = PickBestDeliveryGroup(
                    deliveryGroups,
                    pendingCountByDeliveryGroup,
                    failedDeliveryGroups);

                // Build the result for this incremental plan and update
                // delivery group completion state.
                var completed = ImmutableList.CreateBuilder<CompletedResult>();
                OperationResult payload;

                if (error is not null)
                {
                    var errorObj = ErrorBuilder.New()
                        .SetMessage(error.Message)
                        .Build();
                    payload = OperationResult.FromError(errorObj);
                    MarkDeliveryGroupsFailed(deliveryGroups, [errorObj], ref failedDeliveryGroups);
                    CompleteDeliveryGroups(
                        deliveryGroups,
                        pendingCountByDeliveryGroup,
                        failedDeliveryGroups,
                        completed,
                        errors: [errorObj]);
                }
                else if (result is not null)
                {
                    payload = result;

                    // Use the selected delivery group's path to produce the
                    // incremental result data for this plan.
                    if (bestDeliveryGroup is not null
                        && result.Data.HasValue
                        && !result.Data.Value.IsValueNull
                        && TryCreateIncrementalResults(
                            result.Data.Value,
                            bestDeliveryGroup,
                            operationPlan.GetDeliveryPath(bestDeliveryGroup.Id).PendingFieldCount,
                            result.Errors.Count > 0 ? result.Errors : null,
                            out var incrementalResults))
                    {
                        payload.Incremental = incrementalResults;
                    }

                    var failed = result.Errors.Count > 0 && payload.Incremental.Count == 0;
                    if (failed)
                    {
                        MarkDeliveryGroupsFailed(deliveryGroups, result.Errors, ref failedDeliveryGroups);
                    }

                    CompleteDeliveryGroups(
                        deliveryGroups,
                        pendingCountByDeliveryGroup,
                        failedDeliveryGroups,
                        completed,
                        errors: failed ? result.Errors : null);
                }
                else
                {
                    // Incremental plan with no execution result: all fields may
                    // have been conditional and excluded. Report a successful
                    // completion with no data.
                    var placeholder = ErrorBuilder.New()
                        .SetMessage("placeholder")
                        .Build();
                    payload = OperationResult.FromError(placeholder);
                    CompleteDeliveryGroups(
                        deliveryGroups,
                        pendingCountByDeliveryGroup,
                        failedDeliveryGroups,
                        completed,
                        errors: null);
                }

                // Start nested incremental plans whose enclosing delivery group
                // belongs to the just-completed result and has now completed in
                // every incremental plan that contains it, then mark their delivery
                // groups as pending on the outgoing result.
                var childPending = ImmutableList.CreateBuilder<PendingResult>();
                foreach (var candidate in incrementalPlans)
                {
                    var candidateGroups = deliveryGroupsByPlan[candidate];

                    if (candidateGroups.IsEmpty || started.Contains(candidate))
                    {
                        continue;
                    }

                    if (!IsEnclosingDeliveryGroupCompleted(
                        candidateGroups[0],
                        deliveryGroups,
                        activeDeliveryGroupIds,
                        pendingCountByDeliveryGroup,
                        failedDeliveryGroups))
                    {
                        continue;
                    }

                    // Nested plans resolve requirements from the immediate
                    // enclosing plan when available.
                    var parentStore = scopeContext?.GetResultStoreForChildDefer()
                        ?? rootContext.GetResultStoreForChildDefer();

                    started.Add(candidate);
                    pendingIncrementalPlanCount++;
                    BeginIncrementalPlan(
                        requestContext,
                        variables,
                        policyResult,
                        candidate,
                        candidateGroups,
                        parentStore,
                        channel.Writer,
                        requestArena,
                        cancellationToken);

                    foreach (var deliveryGroup in candidateGroups)
                    {
                        if (!announcedDeliveryGroupIds.Add(deliveryGroup.Id))
                        {
                            continue;
                        }

                        childPending.Add(new PendingResult(
                            deliveryGroup.Id,
                            operationPlan.GetDeliveryPath(deliveryGroup.Id).PendingPath,
                            deliveryGroup.Label));
                    }
                }

                if (emptyDeliveryGroups is not null)
                {
                    foreach (var deliveryGroup in emptyDeliveryGroups)
                    {
                        if (announcedDeliveryGroupIds.Contains(deliveryGroup.Id)
                            || !IsEnclosingDeliveryGroupCompleted(
                                deliveryGroup,
                                deliveryGroups,
                                activeDeliveryGroupIds,
                                pendingCountByDeliveryGroup,
                                failedDeliveryGroups))
                        {
                            continue;
                        }

                        announcedDeliveryGroupIds.Add(deliveryGroup.Id);
                        emptyDeliveryGroupScopes![deliveryGroup.Id] = scopeContext;
                        pendingIncrementalPlanCount++;
                        channel.Writer.TryWrite(new IncrementalPlanResult(null, [deliveryGroup], null, null, null));

                        childPending.Add(new PendingResult(
                            deliveryGroup.Id,
                            operationPlan.GetDeliveryPath(deliveryGroup.Id).PendingPath,
                            deliveryGroup.Label));
                    }
                }

                // Set Completed before clearing top-level errors.
                if (completed.Count > 0)
                {
                    payload.Completed = completed.ToImmutable();
                }

                if (childPending.Count > 0)
                {
                    payload.Pending = childPending.ToImmutable();
                }

                // Incremental results do not carry root data or top-level errors.
                payload.Data = null;
                if (payload.Errors.Count > 0)
                {
                    payload.Errors = [];
                }

                payload.HasNext = pendingIncrementalPlanCount > 0;
                yield return payload;
            }

            // The stream was read to completion, so the initial plan and every incremental plan have
            // finished writing into the single request arena.
            //
            // Sealing it here lets its pages be  returned to the pool when the stream result is disposed.
            // On early disposal or cancellation we never reach this point and the arena is abandoned instead,
            // because an in-flight incremental plan may still write to it.
            requestArena.Seal();
        }
        finally
        {
            // Dispose completed incremental plan contexts after the stream
            // finishes. The root context is owned by the surrounding stream.
            foreach (var incrementalPlanContext in incrementalPlanContexts.Values)
            {
                await incrementalPlanContext.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Starts an incremental plan and publishes completion to the channel.
    /// Plans without execution nodes complete without running work.
    /// </summary>
    private static void BeginIncrementalPlan(
        RequestContext requestContext,
        IVariableValueCollection variables,
        PolicyEvaluationResult policyResult,
        IncrementalPlan incrementalPlan,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        FetchResultStore parentResultStore,
        ChannelWriter<IncrementalPlanResult> completion,
        MemoryArena requestArena,
        CancellationToken cancellationToken)
    {
        if (incrementalPlan.AllNodes.IsEmpty)
        {
            completion.TryWrite(new IncrementalPlanResult(incrementalPlan, deliveryGroups, null, null, null));
            return;
        }

        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var context = requestContext.Schema.Services.GetRequiredService<OperationPlanContextPool>().Rent();

        try
        {
            // Reuse the single request arena instead of minting a per-plan arena: the request
            // executor has already detached it from the request context, so it is supplied
            // explicitly here.
            context.Initialize(
                requestContext,
                variables,
                variableIndex: -1,
                policyResult,
                incrementalPlan,
                executionCts,
                requestArena);

            // Copy parent-scope requirements into the child context.
            try
            {
                CollectIncrementalPlanRequirements(parentResultStore, incrementalPlan, context);
            }
            catch (InvalidOperationException ex)
            {
                // The plan fails with an error and still completes through the channel.
                executionCts.Dispose();
                completion.TryWrite(new IncrementalPlanResult(incrementalPlan, deliveryGroups, context, null, ex));
                return;
            }
        }
        catch
        {
            _ = context.DisposeAsync();
            executionCts.Dispose();
            throw;
        }

        // Ownership of context and executionCts passes to ExecuteIncrementalPlan.
        _ = ExecuteIncrementalPlan(
            context,
            incrementalPlan,
            deliveryGroups,
            executionCts,
            completion,
            cancellationToken);
    }

    private static async Task ExecuteIncrementalPlan(
        OperationPlanContext context,
        IOperationPlan plan,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        CancellationTokenSource executionCts,
        ChannelWriter<IncrementalPlanResult> completion,
        CancellationToken cancellationToken)
    {
        var incrementalPlan = (IncrementalPlan)plan;

        try
        {
            context.Begin();

            await ExecuteQueryAsync(context, plan, executionCts.Token);

            // Keep result resources available for nested incremental plans.
            var deferredResult = context.Complete(retainMemoryForDefer: true);
            await completion.WriteAsync(
                new IncrementalPlanResult(incrementalPlan, deliveryGroups, context, deferredResult, null),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Signal cancellation so the consumer's counter balances; the
            // context is handed back on the channel so the iterator can
            // dispose it as part of its finally-block cleanup.
            await completion.WriteAsync(
                new IncrementalPlanResult(incrementalPlan, deliveryGroups, context, null, null),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            await completion.WriteAsync(
                new IncrementalPlanResult(incrementalPlan, deliveryGroups, context, null, ex),
                CancellationToken.None);
        }
        finally
        {
            executionCts.Dispose();
        }
    }

    /// <summary>
    /// Collects requirements satisfied by the enclosing plan scope and
    /// installs their values on the child context.
    /// </summary>
    private static void CollectIncrementalPlanRequirements(
        FetchResultStore parentResultStore,
        IncrementalPlan incrementalPlan,
        OperationPlanContext childContext)
    {
        var collected = CollectParentScopeRequirements(incrementalPlan);
        if (collected.Count == 0)
        {
            return;
        }

        var anchor = collected[0].Path;
        for (var i = 1; i < collected.Count; i++)
        {
            if (!collected[i].Path.Equals(anchor))
            {
                throw new InvalidOperationException(
                    "Deferred incremental plan has parent-sourced requirements at different anchor paths; "
                    + "one-time materialization assumes a single anchor.");
            }
        }

        var requirementSpan = new OperationRequirement[collected.Count];
        var keys = new HashSet<string>(collected.Count, StringComparer.Ordinal);
        for (var i = 0; i < collected.Count; i++)
        {
            requirementSpan[i] = collected[i];
            keys.Add(collected[i].Key);
        }

        var parentValues = parentResultStore.CreateVariableValueSets(
            anchor,
            requestVariables: [],
            requirementSpan);

        childContext.SetRequirements(parentValues, keys, parentResultStore.Operation);
    }

    private static List<OperationRequirement> CollectParentScopeRequirements(IncrementalPlan incrementalPlan)
    {
        var collected = new List<OperationRequirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in incrementalPlan.AllNodes)
        {
            switch (node)
            {
                case OperationExecutionNode op when !op.ParentDependencies.IsEmpty:
                    AppendRequirements(op.Requirements, collected, seen);
                    break;

                case ApolloOperationExecutionNode apolloOp when !apolloOp.ParentDependencies.IsEmpty:
                    AppendRequirements(apolloOp.Requirements, collected, seen);
                    break;

                case OperationBatchExecutionNode batch:
                    foreach (var definition in batch.Operations)
                    {
                        if (!definition.ParentDependencies.IsEmpty)
                        {
                            AppendRequirements(definition.Requirements, collected, seen);
                        }
                    }
                    break;

                case ApolloOperationBatchExecutionNode apolloBatch:
                    foreach (var definition in apolloBatch.Operations)
                    {
                        if (!definition.ParentDependencies.IsEmpty)
                        {
                            AppendRequirements(definition.Requirements, collected, seen);
                        }
                    }
                    break;
            }
        }

        return collected;
    }

    private static void AppendRequirements(
        ReadOnlySpan<OperationRequirement> requirements,
        List<OperationRequirement> collected,
        HashSet<string> seen)
    {
        foreach (var requirement in requirements)
        {
            if (seen.Add(requirement.Key))
            {
                collected.Add(requirement);
            }
        }
    }

    private static void EnsureDeferIfVariablesAreNotNull(
        IVariableValueCollection variables,
        OperationPlan operationPlan)
    {
        foreach (var deliveryGroup in operationPlan.DeliveryGroups)
        {
            if (deliveryGroup.IfVariable is { } name
                && variables.TryGetValue<NullValueNode>(name, out _))
            {
                throw ThrowHelper.DeferIfVariableIsNull(name);
            }
        }
    }

    private static DeliveryGroup? GetEnabledDeliveryGroup(
        DeliveryGroup? deliveryGroup,
        HashSet<int> activeDeliveryGroupIds)
    {
        while (deliveryGroup is not null && !activeDeliveryGroupIds.Contains(deliveryGroup.Id))
        {
            deliveryGroup = deliveryGroup.Parent;
        }

        return deliveryGroup;
    }

    private static Dictionary<IncrementalPlan, ImmutableArray<DeliveryGroup>> ResolveDeliveryGroupsByPlan(
        OperationPlan operationPlan,
        HashSet<int> activeDeliveryGroupIds)
    {
        var deliveryGroupsByPlan = new Dictionary<IncrementalPlan, ImmutableArray<DeliveryGroup>>(
            operationPlan.IncrementalPlans.Length);

        foreach (var incrementalPlan in operationPlan.IncrementalPlans)
        {
            var allActive = true;

            foreach (var deliveryGroup in incrementalPlan.DeliveryGroups)
            {
                if (!activeDeliveryGroupIds.Contains(deliveryGroup.Id))
                {
                    allActive = false;
                    break;
                }
            }

            if (allActive)
            {
                deliveryGroupsByPlan[incrementalPlan] = incrementalPlan.DeliveryGroups;
                continue;
            }

            // A disabled group is replaced by its nearest enabled enclosing group. A group enclosed
            // by another group of the plan is dropped, because the plan delivers with the enclosing one.
            var enabled = new List<DeliveryGroup>(incrementalPlan.DeliveryGroups.Length);

            foreach (var deliveryGroup in incrementalPlan.DeliveryGroups)
            {
                var enabledGroup = GetEnabledDeliveryGroup(deliveryGroup, activeDeliveryGroupIds);

                if (enabledGroup is not null && !ContainsDeliveryGroup(enabled, enabledGroup.Id))
                {
                    enabled.Add(enabledGroup);
                }
            }

            var resolved = new List<DeliveryGroup>(enabled.Count);

            foreach (var deliveryGroup in enabled)
            {
                var enclosed = false;

                for (var ancestor = deliveryGroup.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ContainsDeliveryGroup(enabled, ancestor.Id))
                    {
                        enclosed = true;
                        break;
                    }
                }

                if (!enclosed)
                {
                    resolved.Add(deliveryGroup);
                }
            }

            resolved.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            deliveryGroupsByPlan[incrementalPlan] = [.. resolved];
        }

        return deliveryGroupsByPlan;
    }

    private static bool ContainsDeliveryGroup(List<DeliveryGroup> deliveryGroups, int id)
    {
        foreach (var deliveryGroup in deliveryGroups)
        {
            if (deliveryGroup.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEnclosingDeliveryGroupCompleted(
        DeliveryGroup deliveryGroup,
        ImmutableArray<DeliveryGroup> completedDeliveryGroups,
        HashSet<int> activeDeliveryGroupIds,
        Dictionary<int, int> pendingCountByDeliveryGroup,
        Dictionary<int, IReadOnlyList<IError>>? failedDeliveryGroups)
    {
        var enclosing = GetEnabledDeliveryGroup(deliveryGroup.Parent, activeDeliveryGroupIds);

        if (enclosing is null || failedDeliveryGroups?.ContainsKey(enclosing.Id) is true)
        {
            return false;
        }

        foreach (var completed in completedDeliveryGroups)
        {
            if (completed.Id == enclosing.Id)
            {
                return !pendingCountByDeliveryGroup.ContainsKey(enclosing.Id);
            }
        }

        return false;
    }

    /// <summary>
    /// Selects the delivery group used to anchor an incremental result.
    /// The group with the deepest path is selected. Ties are broken by the group this result completes,
    /// then by the smallest <see cref="DeliveryGroup.Id"/>. Failed groups are skipped, and
    /// <see langword="null"/> is returned when every group has failed.
    /// </summary>
    private static DeliveryGroup? PickBestDeliveryGroup(
        ImmutableArray<DeliveryGroup> deliveryGroups,
        Dictionary<int, int> pendingCountByDeliveryGroup,
        Dictionary<int, IReadOnlyList<IError>>? failedDeliveryGroups)
    {
        var skipFailed = false;

        if (failedDeliveryGroups is not null)
        {
            var hasNotFailed = false;

            foreach (var deliveryGroup in deliveryGroups)
            {
                if (!failedDeliveryGroups.ContainsKey(deliveryGroup.Id))
                {
                    hasNotFailed = true;
                    break;
                }
            }

            if (!hasNotFailed && !deliveryGroups.IsEmpty)
            {
                return null;
            }

            skipFailed = hasNotFailed;
        }

        DeliveryGroup? best = null;
        var bestLength = 0;
        var bestCompletes = false;

        foreach (var candidate in deliveryGroups)
        {
            if (skipFailed && failedDeliveryGroups!.ContainsKey(candidate.Id))
            {
                continue;
            }

            var candidateLength = candidate.Path?.Length ?? 0;
            var candidateCompletes = IsCompleting(candidate, pendingCountByDeliveryGroup);

            if (best is null)
            {
                best = candidate;
                bestLength = candidateLength;
                bestCompletes = candidateCompletes;
                continue;
            }

            if (candidateLength < bestLength)
            {
                continue;
            }

            if (candidateLength > bestLength
                || (candidateCompletes && !bestCompletes)
                || (candidateCompletes == bestCompletes && candidate.Id < best.Id))
            {
                best = candidate;
                bestLength = candidateLength;
                bestCompletes = candidateCompletes;
            }
        }

        return best;
    }

    private static bool IsCompleting(
        DeliveryGroup deliveryGroup,
        Dictionary<int, int> pendingCountByDeliveryGroup)
        => pendingCountByDeliveryGroup.TryGetValue(deliveryGroup.Id, out var count)
            && count <= 1;

    private static void MarkDeliveryGroupsFailed(
        ImmutableArray<DeliveryGroup> deliveryGroups,
        IReadOnlyList<IError> errors,
        ref Dictionary<int, IReadOnlyList<IError>>? failedDeliveryGroups)
    {
        failedDeliveryGroups ??= [];

        foreach (var deliveryGroup in deliveryGroups)
        {
            failedDeliveryGroups.TryAdd(deliveryGroup.Id, errors);
        }
    }

    private static void CompleteDeliveryGroups(
        ImmutableArray<DeliveryGroup> deliveryGroups,
        Dictionary<int, int> pendingCountByDeliveryGroup,
        Dictionary<int, IReadOnlyList<IError>>? failedDeliveryGroups,
        ImmutableList<CompletedResult>.Builder completed,
        IReadOnlyList<IError>? errors)
    {
        foreach (var deliveryGroup in deliveryGroups)
        {
            if (!pendingCountByDeliveryGroup.TryGetValue(deliveryGroup.Id, out var count))
            {
                continue;
            }

            count--;
            if (count <= 0)
            {
                pendingCountByDeliveryGroup.Remove(deliveryGroup.Id);

                if (failedDeliveryGroups is null
                    || !failedDeliveryGroups.TryGetValue(deliveryGroup.Id, out var groupErrors))
                {
                    groupErrors = errors;
                }

                completed.Add(groupErrors is { Count: > 0 }
                    ? new CompletedResult(deliveryGroup.Id, groupErrors)
                    : new CompletedResult(deliveryGroup.Id));
            }
            else
            {
                pendingCountByDeliveryGroup[deliveryGroup.Id] = count;
            }
        }
    }

    /// <summary>
    /// Produces <see cref="IncrementalObjectResult"/> entries whose logical root
    /// is the subtree at the best delivery group's path within the deferred
    /// plan's composite result. The incremental delivery contract requires
    /// <c>incremental.data</c> to be a map of fields to merge at the pending
    /// path, not the fully rooted result. When the delivery path crosses a list,
    /// the pending path ends at the first list and each concrete descendant is
    /// emitted with a relative field-and-index <c>subPath</c>.
    /// </summary>
    private static bool TryCreateIncrementalResults(
        OperationResultData rootData,
        DeliveryGroup bestDeliveryGroup,
        int pendingFieldCount,
        ImmutableList<IError>? errors,
        out ImmutableList<IIncrementalResult> incrementalResults)
    {
        if (rootData.Value is not CompositeResultDocument document)
        {
            throw new InvalidOperationException(
                "Deferred result data must be backed by a composite result document.");
        }

        var selectionPath = bestDeliveryGroup.Path ?? SelectionPath.Root;
        var builder = ImmutableList.CreateBuilder<IIncrementalResult>();

        CollectIncrementalResults(
            document,
            document.Data,
            selectionPath,
            segmentIndex: 0,
            fieldIndex: 0,
            pendingFieldCount,
            Path.Root,
            bestDeliveryGroup.Id,
            errors,
            builder,
            claimRows: false);

        incrementalResults = builder.ToImmutable();
        return incrementalResults.Count > 0;
    }

    private static void CollectIncrementalResults(
        CompositeResultDocument document,
        CompositeResultElement element,
        SelectionPath selectionPath,
        int segmentIndex,
        int fieldIndex,
        int pendingFieldCount,
        Path subPath,
        int deliveryGroupId,
        ImmutableList<IError>? errors,
        ImmutableList<IIncrementalResult>.Builder results,
        bool claimRows)
    {
        if (element.IsNullMarker)
        {
            if (HasRemainingFieldSegments(selectionPath, segmentIndex))
            {
                return;
            }
        }
        else if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (element.ValueKind is JsonValueKind.Array && !element.IsNullMarker)
        {
            var length = element.GetArrayLength();

            for (var i = 0; i < length; i++)
            {
                CollectIncrementalResults(
                    document,
                    element[i],
                    selectionPath,
                    segmentIndex,
                    fieldIndex,
                    pendingFieldCount,
                    subPath.Append(i),
                    deliveryGroupId,
                    errors,
                    results,
                    claimRows);
            }

            return;
        }

        if (segmentIndex == selectionPath.Length)
        {
            if (claimRows)
            {
                var roots = new List<(CompositeResultDocument.Cursor Cursor, Path Path)>();

                if (!document.ClaimDeliveryRows(element.Cursor, deliveryGroupId, subPath, roots))
                {
                    return;
                }

                foreach (var (cursor, path) in roots)
                {
                    results.Add(
                        new IncrementalObjectResult(
                            deliveryGroupId,
                            errors,
                            path.IsRoot ? null : path,
                            new OperationResultData(
                                document,
                                isValueNull: false,
                                new DeliveryPayloadDataFormatter(document, cursor, deliveryGroupId),
                                memoryHolder: null)));
                }

                return;
            }

            results.Add(
                new IncrementalObjectResult(
                    deliveryGroupId,
                    errors,
                    subPath.IsRoot ? null : subPath,
                    CreateIncrementalData(document, element)));
            return;
        }

        var segment = selectionPath[segmentIndex];

        if (segment.Kind is not SelectionPathSegmentKind.Field)
        {
            CollectIncrementalResults(
                document,
                element,
                selectionPath,
                segmentIndex + 1,
                fieldIndex,
                pendingFieldCount,
                subPath,
                deliveryGroupId,
                errors,
                results,
                claimRows);
            return;
        }

        if (element.ValueKind is not JsonValueKind.Object
            || !element.TryGetProperty(segment.Name, out var next))
        {
            return;
        }

        CollectIncrementalResults(
            document,
            next,
            selectionPath,
            segmentIndex + 1,
            fieldIndex + 1,
            pendingFieldCount,
            fieldIndex < pendingFieldCount ? subPath : subPath.Append(segment.Name),
            deliveryGroupId,
            errors,
            results,
            claimRows);
    }

    private static bool HasRemainingFieldSegments(SelectionPath selectionPath, int segmentIndex)
    {
        for (var i = segmentIndex; i < selectionPath.Length; i++)
        {
            if (selectionPath[i].Kind is SelectionPathSegmentKind.Field)
            {
                return true;
            }
        }

        return false;
    }

    private static OperationResultData CreateIncrementalData(
        CompositeResultDocument document,
        CompositeResultElement element)
        // MemoryHolder is intentionally not carried over. The surrounding
        // OperationResult already owns the composite document's lifetime, and
        // the IncrementalObjectResult is a non-owning view over it.
        => new(
            document,
            isValueNull: element.IsNullMarker,
            new DeferredPayloadDataFormatter(element),
            memoryHolder: null);

    public static async Task<IExecutionResult> SubscribeAsync(
        RequestContext requestContext,
        OperationPlan operationPlan,
        CancellationToken cancellationToken)
    {
        // subscription plans must have a single root,
        // which represents the subscription to a source schema.
        var root = operationPlan.RootNodes.Single();

        // We create a new CancellationTokenSource that can be used to halt the execution engine,
        // without also cancelling the entire request pipeline.
        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        OperationPlanContext? context = null;
        CancellationTokenRegistration? cancellationRegistration = null;

        try
        {
            context = requestContext.Schema.Services.GetRequiredService<OperationPlanContextPool>().Rent();
            context.Initialize(
                requestContext,
                requestContext.VariableValues[0],
                variableIndex: -1,
                requestContext.GetPolicyEvaluationResult(0),
                operationPlan,
                executionCts);

            var subscriptionResult = root switch
            {
                OperationExecutionNode subscriptionNode => subscriptionNode.Subscribe(context),
                EventStreamExecutionNode eventStreamNode => eventStreamNode.Subscribe(context),
                _ => throw new InvalidOperationException("The specified operation plan is not supported.")
            };
            var executionState = context.ExecutionState;

            cancellationRegistration = executionCts.Token.Register(
                static state => Unsafe.As<AsyncAutoResetEvent>(state)!.TryResetToIdle(),
                executionState.Signal);

            if (subscriptionResult.Status is not ExecutionStatus.Success)
            {
                throw new InvalidOperationException("We could not subscribe to the underlying source schema.");
            }

            // The subscription setup is complete and nothing writes into the subscribe-scoped request
            // arena anymore; each event rents its own arena. Sealing it here lets its pages be returned
            // to the pool once the subscription result is disposed. If the setup fails we never get here
            // and the unsealed arena is abandoned instead.
            requestContext.Memory?.Seal();

            var subscriptionEnumerable = CreateResponseStream(
                context,
                root,
                subscriptionResult,
                requestContext.Schema.Services.GetService<ExecutionConcurrencyGate>(),
                requestContext.Schema.GetRequestOptions().ExecutionTimeout,
                executionCts.Token,
                cancellationToken);

            var stream = new ResponseStream(() => subscriptionEnumerable);
            stream.RegisterForCleanup(context);
            stream.RegisterForCleanup(executionCts);
            return stream;
        }
        catch (Exception)
        {
            executionCts.Dispose();

            if (cancellationRegistration is { } r)
            {
                await r.DisposeAsync();
            }

            if (context is { } c)
            {
                await c.DisposeAsync();
            }

            throw;
        }
    }

    private static async Task ExecuteQueryAsync(
        OperationPlanContext context,
        IOperationPlan plan,
        CancellationToken cancellationToken)
    {
        var executionState = context.ExecutionState;

        await using var cancellationRegistration = cancellationToken.Register(
            static state => Unsafe.As<AsyncAutoResetEvent>(state)!.TryResetToIdle(),
            executionState.Signal);

        // GraphQL queries allow us to execute the plan by using full parallelism.
        // We fill the backlog with all nodes from the operation plan.
        executionState.FillBacklog(plan);

        // Then we start all root nodes as they can be processed in parallel.
        foreach (var root in plan.RootNodes)
        {
            executionState.StartNode(context, root, cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested && executionState.IsProcessing())
        {
            while (executionState.TryDequeuePendingMerge(out var merge))
            {
                executionState.ApplyMerge(context, merge);
            }

            while (executionState.TryDequeueCompletedResult(out var result))
            {
                while (executionState.TryDequeuePendingMerge(out var merge))
                {
                    executionState.ApplyMerge(context, merge);
                }

                result = executionState.ApplyPendingMergeFailure(result);
                var node = plan.GetNodeById(result.Id);
                executionState.CompleteNode(plan, node, result);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                executionState.EnqueueNextNodes(context, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested || !executionState.IsProcessing())
            {
                break;
            }

            // The signal will be set every time a node completes and will release the executor
            // from the async wait to go through the completed results.
            await executionState.Signal;
        }

        await DrainActiveNodesAsync(plan, executionState);

        if (context.CollectTelemetry)
        {
            context.Traces = executionState.Traces.ToImmutableDictionary();
        }
    }

    private static async Task ExecuteMutationAsync(
        OperationPlanContext context,
        IOperationPlan plan,
        CancellationToken cancellationToken)
    {
        var executionState = context.ExecutionState;

        await using var cancellationRegistration = cancellationToken.Register(
            static state => Unsafe.As<AsyncAutoResetEvent>(state)!.TryResetToIdle(),
            executionState.Signal);

        // For mutations, we fill the backlog with all nodes from the operation plan just like for queries.
        executionState.FillBacklog(plan);

        // The difference here is that the planner has one root node for each mutation field.
        // We execute the root nodes one after the other to cater for the GraphQL spec mutation algorithm
        // that requires sequential execution the roots but allows for parallel execution of their subtrees.
        foreach (var root in plan.RootNodes)
        {
            // We start the first root ...
            executionState.StartNode(context, root, cancellationToken);

            // ... and then process the subtree until its complete.
            // This is why in the mutation algorithm check for `HasActiveNodes` instead of `IsProcessing` which
            // would be true as long as there are items on the backlog. `HasActiveNodes` however will be true
            // as long as there are active nodes that result from processing the current subtree.
            while (!cancellationToken.IsCancellationRequested && executionState.HasActiveNodes())
            {
                while (executionState.TryDequeuePendingMerge(out var merge))
                {
                    executionState.ApplyMerge(context, merge);
                }

                while (executionState.TryDequeueCompletedResult(out var result))
                {
                    while (executionState.TryDequeuePendingMerge(out var merge))
                    {
                        executionState.ApplyMerge(context, merge);
                    }

                    result = executionState.ApplyPendingMergeFailure(result);
                    var node = plan.GetNodeById(result.Id);
                    executionState.CompleteNode(plan, node, result);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    executionState.EnqueueNextNodes(context, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested || !executionState.HasActiveNodes())
                {
                    break;
                }

                // The signal will be set every time a node completes and will release the executor
                // from the async wait to go through the completed results.
                await executionState.Signal;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        await DrainActiveNodesAsync(plan, executionState);

        if (context.CollectTelemetry)
        {
            context.Traces = executionState.Traces.ToImmutableDictionary();
        }
    }

    private static async IAsyncEnumerable<OperationResult> CreateResponseStream(
        OperationPlanContext context,
        ExecutionNode subscriptionNode,
        SubscriptionResult subscriptionResult,
        ExecutionConcurrencyGate? concurrencyGate,
        TimeSpan eventTimeout,
        [EnumeratorCancellation] CancellationToken executionCancellationToken,
        CancellationToken requestCancellationToken)
    {
        var plan = context.OperationPlan;
        var executionState = context.ExecutionState;

        await using var cancellationRegistration = executionCancellationToken.Register(
            static state => Unsafe.As<AsyncAutoResetEvent>(state)!.TryResetToIdle(),
            executionState.Signal);

        // We allocate one CancellationTokenSource per subscription and reuse it across
        // healthy events via TryReset(). If a cancellation is requested because of
        // null-propagation to the root, we replace the source before the next event.
        // The execution token is linked in so that client-abort / server-shutdown still
        // propagates.
        var (eventCts, eventCtsRegistration) = CreateEventCancellation();

        var schemaName = GetSubscriptionSchemaName(context, subscriptionNode);

        try
        {
            await foreach (var eventArgs in subscriptionResult.ReadStreamAsync()
                .WithCancellation(executionCancellationToken))
            {
                using var scope = context.DiagnosticEvents.OnSubscriptionEvent(
                    context,
                    subscriptionNode,
                    schemaName,
                    subscriptionResult.Id);

                OperationResult result;

                var gateAcquired = false;

                // An error reported between events (a failed source read) can null-propagate
                // to the root and cancel the shared event source before this event is armed.
                // A cancelled source cannot be reset, so swap in a fresh one (and re-link
                // client-abort / shutdown) so that the event starts with a live timeout budget.
                if (eventCts.IsCancellationRequested && !executionCancellationToken.IsCancellationRequested)
                {
                    await eventCtsRegistration.DisposeAsync();
                    eventCts.Dispose();
                    (eventCts, eventCtsRegistration) = CreateEventCancellation();
                }

                // Arm the shared CTS for this event and derive the per-event token so
                // that each event is bounded by the configured execution timeout.
                eventCts.CancelAfter(eventTimeout);
                var eventToken = eventCts.Token;

                try
                {
                    if (concurrencyGate is { IsEnabled: true })
                    {
                        await concurrencyGate.WaitAsync(eventToken).ConfigureAwait(false);
                        gateAcquired = true;
                    }

                    context.Begin(eventArgs.StartTimestamp, eventArgs.Activity?.TraceId.ToHexString());

                    executionState.Reset();

                    executionState.FillBacklog(plan);
                    executionState.EnqueueForCompletion(
                        new ExecutionNodeResult(
                            subscriptionNode.Id,
                            eventArgs.Activity,
                            eventArgs.Status,
                            eventArgs.Duration,
                            Exception: null,
                            DependentsToExecute: eventArgs.DependentsToExecute,
                            SkippedDefinitions: [],
                            VariableValueSets: eventArgs.VariableValueSets));

                    while (!eventToken.IsCancellationRequested && executionState.IsProcessing())
                    {
                        while (executionState.TryDequeuePendingMerge(out var merge))
                        {
                            executionState.ApplyMerge(context, merge);
                        }

                        while (executionState.TryDequeueCompletedResult(out var nodeResult))
                        {
                            while (executionState.TryDequeuePendingMerge(out var merge))
                            {
                                executionState.ApplyMerge(context, merge);
                            }

                            nodeResult = executionState.ApplyPendingMergeFailure(nodeResult);
                            var node = plan.GetNodeById(nodeResult.Id);
                            executionState.CompleteNode(plan, node, nodeResult);
                        }

                        if (!eventToken.IsCancellationRequested)
                        {
                            executionState.EnqueueNextNodes(context, eventToken);
                        }

                        if (eventToken.IsCancellationRequested || !executionState.IsProcessing())
                        {
                            break;
                        }

                        // The signal will be set every time a node completes and will release the executor
                        // from the async wait to go through the completed results.
                        await executionState.Signal;
                    }

                    // The context is shared across events and the next event resets the
                    // execution state, so we must let this event's in-flight sibling nodes
                    // finish and account for their completions before that reset. Otherwise
                    // a late completion would poison the next event.
                    await DrainActiveNodesAsync(plan, executionState);

                    // If the original CancellationToken of the request was cancelled,
                    // the Execution nodes and the PlanExecutor should have been gracefully cancelled,
                    // so we throw here to properly cancel the request execution.
                    requestCancellationToken.ThrowIfCancellationRequested();

                    // If the event token was cancelled by a genuine timeout or abort, tear the
                    // stream down. A root-null halt also cancels the event token, but that is benign
                    // (the result is a settled {data: null, errors: [...]}), so we keep the stream
                    // alive and let context.Complete() produce it.
                    if (!executionState.ProcessingCompletedEarly)
                    {
                        eventToken.ThrowIfCancellationRequested();
                    }

                    result = context.Complete(reusable: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.DiagnosticEvents.SubscriptionEventError(
                        context,
                        subscriptionNode,
                        schemaName,
                        subscriptionResult.Id,
                        ex);
                    throw;
                }
                finally
                {
                    // disposing the eventArgs disposes the telemetry scope.
                    eventArgs.Dispose();

                    if (gateAcquired)
                    {
                        concurrencyGate!.Release();
                    }

                    if (executionState.ProcessingCompletedEarly)
                    {
                        // A root-null halt cancelled the event source. A cancelled source cannot be
                        // reset, so swap in a fresh one (and re-link client-abort / shutdown) for the
                        // next event. This only happens on events that null-propagate to the root.
                        await eventCtsRegistration.DisposeAsync();
                        eventCts.Dispose();
                        (eventCts, eventCtsRegistration) = CreateEventCancellation();
                    }
                    else
                    {
                        // Healthy event: reset the shared source so the next event starts with a
                        // fresh timeout budget and no allocation. If TryReset() returns false the
                        // source was cancelled by a timeout or client-abort; the thrown
                        // OperationCanceledException has already propagated and the enumerator
                        // surfaces the teardown.
                        eventCts.TryReset();
                    }
                }

                yield return result;

                // Execution resumes here only after the consumer finished writing the
                // yielded result to the client and asked for the next event. Record the
                // delivery before the event scope is disposed at the end of this loop
                // iteration, so a client abort that races the resume cannot erase the
                // delivered event's success status. An event whose write failed never
                // resumes the stream and therefore is never reported as delivered.
                context.DiagnosticEvents.SubscriptionEventDelivered(
                    context,
                    subscriptionNode,
                    schemaName,
                    subscriptionResult.Id);
            }
        }
        finally
        {
            await eventCtsRegistration.DisposeAsync();
            eventCts?.Dispose();
        }

        // Creates a fresh event-scoped cancellation source, links client-abort / shutdown into it,
        // and installs it as the engine's cancellation source for the next event.
        (CancellationTokenSource Source, CancellationTokenRegistration Registration) CreateEventCancellation()
        {
            var cts = new CancellationTokenSource();
            var registration = executionCancellationToken.UnsafeRegister(
                static state => Unsafe.As<CancellationTokenSource>(state)!.Cancel(),
                cts);
            executionState.SetCancellationSource(cts);
            return (cts, registration);
        }
    }

    private static async ValueTask DrainActiveNodesAsync(
        IOperationPlan plan,
        ExecutionState executionState)
    {
        // When execution halts early (a field error null-bubbled to the root and
        // cancelled the execution, or the request itself was cancelled) sibling node
        // tasks may still be in flight. They close over the pooled context, so we
        // must let them finish and account for their completions before the context
        // returns to the pool. Otherwise a late completion would mutate the state of
        // the next request that reuses this context.
        while (executionState.HasActiveNodes())
        {
            while (executionState.TryDequeueCompletedResult(out var result))
            {
                var node = plan.GetNodeById(result.Id);
                executionState.CompleteNode(plan, node, result);
            }

            if (!executionState.HasActiveNodes())
            {
                break;
            }

            await executionState.Signal;
        }
    }

    private static string GetSubscriptionSchemaName(
        OperationPlanContext context,
        ExecutionNode subscriptionNode)
        => subscriptionNode.SchemaName
            ?? (subscriptionNode is EventStreamExecutionNode ? "event-stream" : context.GetDynamicSchemaName(subscriptionNode));
}
