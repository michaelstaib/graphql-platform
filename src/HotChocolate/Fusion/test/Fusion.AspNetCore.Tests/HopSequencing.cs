using System.Collections.Concurrent;
using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using Microsoft.AspNetCore.TestHost;

namespace HotChocolate.Fusion;

internal sealed class HopSequence
{
    private HopRun _run = new([], heldHop: null);

    // Starts a run that delivers the hops in the given order and holds the held hop until Release is called.
    public void Start(string[] order, string? heldHop = null) => _run = new HopRun(order, heldHop);

    public HopRun Current => _run;

    public void Release() => _run.Release();

    public Task WaitForTurnAsync(string hop, CancellationToken cancellationToken)
        => _run.WaitForTurnAsync(hop, cancellationToken);

    public void MarkDelivered(string hop) => _run.MarkDelivered(hop);
}

internal sealed class HopRun(string[] order, string? heldHop)
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource[] _delivered = order
        .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
        .ToArray();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _requested = new();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void MarkRequested(string hop) => GetRequested(hop).TrySetResult();

    public Task WaitForRequestAsync(string hop, CancellationToken cancellationToken)
        => GetRequested(hop).Task.WaitAsync(s_timeout, cancellationToken);

    public async Task WaitForTurnAsync(string hop, CancellationToken cancellationToken)
    {
        if (hop == heldHop)
        {
            await _released.Task.WaitAsync(s_timeout, cancellationToken);
        }

        var index = Array.IndexOf(order, hop);

        if (index > 0)
        {
            await _delivered[index - 1].Task.WaitAsync(s_timeout, cancellationToken);
        }
    }

    public void MarkDelivered(string hop)
    {
        var index = Array.IndexOf(order, hop);

        if (index >= 0)
        {
            _delivered[index].TrySetResult();
        }
    }

    public void Release() => _released.TrySetResult();

    private TaskCompletionSource GetRequested(string hop)
        => _requested.GetOrAdd(
            hop,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

// A hop that is answered in the order of the current run, after the request of waitForRequestOf was sent. It is
// delivered once its response is read, or, when deliveredOnResponse is false, once a HopMergeListener reports it.
internal sealed class SequencedHop(
    HopSequence sequence,
    string hop,
    string? waitForRequestOf = null,
    bool deliveredOnResponse = true)
{
    private HttpMessageInvoker? _invoker;

    public void Connect(TestServer server)
        => _invoker = new HttpMessageInvoker(server.CreateHandler());

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = sequence.Current;
        run.MarkRequested(hop);

        if (waitForRequestOf is not null)
        {
            await run.WaitForRequestAsync(waitForRequestOf, cancellationToken);
        }

        await run.WaitForTurnAsync(hop, cancellationToken);
        var response = await _invoker!.SendAsync(request, cancellationToken);
        await response.Content.LoadIntoBufferAsync();

        if (deliveredOnResponse)
        {
            run.MarkDelivered(hop);
        }

        return response;
    }
}

// Delivers the hop of a source schema in the run that was current when its node started, once the node has
// queued its result for merging, so that the result is merged before the result of any hop answered after it.
internal sealed class HopMergeListener(HopSequence sequence, Dictionary<string, string> hopsBySchema)
    : FusionExecutionDiagnosticEventListener
{
    public override IDisposable ExecuteOperationNode(
        OperationPlanContext context,
        OperationExecutionNode node,
        string schemaName)
        => TryDeliver(schemaName) ?? base.ExecuteOperationNode(context, node, schemaName);

    public override IDisposable ExecuteOperationBatchNode(
        OperationPlanContext context,
        OperationBatchExecutionNode node,
        string schemaName)
        => TryDeliver(schemaName) ?? base.ExecuteOperationBatchNode(context, node, schemaName);

    private Delivery? TryDeliver(string schemaName)
        => hopsBySchema.TryGetValue(schemaName, out var hop) ? new Delivery(sequence.Current, hop) : null;

    private sealed class Delivery(HopRun run, string hop) : IDisposable
    {
        public void Dispose() => run.MarkDelivered(hop);
    }
}
