using System.Collections.Concurrent;
using HotChocolate.Execution;
using HotChocolate.Fusion.Diagnostics;

namespace HotChocolate.Fusion.Authorization;

internal sealed class CapturingExecutionDiagnosticEventListener : FusionExecutionDiagnosticEventListener
{
    private readonly ConcurrentQueue<Exception> _requestErrors = new();

    public IReadOnlyCollection<Exception> RequestErrors => _requestErrors;

    public override void RequestError(RequestContext context, Exception error)
        => _requestErrors.Enqueue(error);
}
