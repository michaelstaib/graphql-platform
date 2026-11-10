using System.Collections.Immutable;
using HotChocolate.Buffers;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Transport.Http;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Clients;

public sealed partial class HttpSourceSchemaClient
{
    /// <summary>
    /// Gets whether <see cref="ExecuteBufferedAsync"/> can execute a request of the given shape.
    /// Subscriptions, file uploads and variable value sets that are merged into one aliased
    /// operation cannot.
    /// </summary>
    /// <param name="operationType">The operation type of the request.</param>
    /// <param name="requiresFileUpload">Whether the request carries file uploads.</param>
    /// <param name="variableCount">The number of variable value sets of the request.</param>
    internal bool CanExecuteBuffered(OperationType operationType, bool requiresFileUpload, int variableCount)
        => operationType is not OperationType.Subscription
            && !requiresFileUpload
            && !(_useAliasBatchingForVariableSets && variableCount > 1);

    /// <summary>
    /// Executes a request and hands its results to <paramref name="sink"/> in the order they are read.
    /// The results are the same as those of <see cref="ExecuteAsync"/>.
    /// </summary>
    internal async ValueTask ExecuteBufferedAsync(
        OperationPlanContext context,
        SourceSchemaClientRequest request,
        ISourceSchemaResultSink sink,
        CancellationToken cancellationToken)
    {
        var node = request.Node;
        var variables = request.Variables;
        ChunkedArrayWriter? buffer = null;
        GraphQLHttpResponse? httpResponse = null;

        try
        {
            var response = SendRequestAsync(context, in request, ref buffer, cancellationToken);
            httpResponse = await response.ConfigureAwait(false);
            context.TrackTransport(node, _configuration.BaseAddress, httpResponse.RawContentType);

            if (variables.Length <= 1)
            {
                var document = await httpResponse
                    .ReadAsResultAsync(context.Memory, cancellationToken)
                    .ConfigureAwait(false);
                OnResult(context, node, sink, requestIndex: 0, CreateSingleResult(variables, document));
                return;
            }

            var mapper = new VariableBatchResultMapper(variables, _supportsVariableBatching);

            await foreach (var document in httpResponse
                .ReadAsResultStreamAsync(context.MemorySource)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!mapper.TryMap(document, out var result))
                {
                    break;
                }

                if (!OnResult(context, node, sink, requestIndex: 0, result))
                {
                    return;
                }
            }

            for (var i = 0; i < mapper.ErrorResultCount; i++)
            {
                if (!OnResult(context, node, sink, requestIndex: 0, mapper.CreateErrorResult(i)))
                {
                    return;
                }
            }
        }
        finally
        {
            httpResponse?.Dispose();
            buffer?.Dispose();
        }
    }

    /// <summary>
    /// Gets whether <see cref="ExecuteBatchBufferedAsync"/> can execute the batch. Batches that are
    /// merged into one aliased operation, sent as single requests, carry file uploads or contain a
    /// subscription cannot.
    /// </summary>
    internal bool CanExecuteBatchBuffered(ImmutableArray<SourceSchemaClientRequest> requests)
        => !_useAliasBatchingForOperationBatches
            && Capabilities.HasFlag(SourceSchemaClientCapabilities.RequestBatching)
            && requests.Length > 0
            && !requests.ItemRef(0).RequiresFileUpload
            && !ContainsSubscriptionRequest(requests);

    /// <summary>
    /// Executes a batch as one operation batch request and hands its results to <paramref name="sink"/>
    /// in the order they are read. The results are the same as those of <see cref="ExecuteBatchAsync"/>.
    /// </summary>
    internal async ValueTask ExecuteBatchBufferedAsync(
        OperationPlanContext context,
        ImmutableArray<SourceSchemaClientRequest> requests,
        ISourceSchemaResultSink sink,
        CancellationToken cancellationToken)
    {
        GraphQLHttpRequest? httpRequest = null;
        ChunkedArrayWriter? buffer = null;

        // The batch body is written straight from the requests when the batch is sent.
        if (!CanSendBatchDirectly(requests))
        {
            (httpRequest, buffer) = CreateBufferedHttpBatchRequest(context, requests);
        }

        GraphQLHttpResponse? httpResponse = null;

        try
        {
            httpResponse = await SendBatchRequestAsync(context, requests, httpRequest, cancellationToken)
                .ConfigureAwait(false);
            context.TrackTransport(requests.ItemRef(0).Node, _configuration.BaseAddress, httpResponse.RawContentType);

            var mapper = new BatchResultMapper(requests, _supportsVariableBatching);

            await foreach (var document in httpResponse
                .ReadAsResultStreamAsync(context.MemorySource)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                mapper.Begin(document);

                while (mapper.TryGetNext(out var requestIndex, out var result))
                {
                    if (!OnResult(context, requests.ItemRef(requestIndex).Node, sink, requestIndex, result))
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            httpResponse?.Dispose();
            buffer?.Dispose();
        }
    }

    private (GraphQLHttpRequest HttpRequest, ChunkedArrayWriter? Buffer) CreateBufferedHttpBatchRequest(
        OperationPlanContext context,
        ImmutableArray<SourceSchemaClientRequest> requests)
    {
        ChunkedArrayWriter? buffer = null;

        try
        {
            ref readonly var firstRequest = ref requests.ItemRef(0);
            var httpRequest = CreateHttpBatchRequest(context, requests, firstRequest.RequiresFileUpload, ref buffer);
            SourceSchemaCallbackHelper.ConfigureCallbacks(httpRequest, context, firstRequest.Node, _configuration);
            return (httpRequest, buffer);
        }
        catch
        {
            buffer?.Dispose();
            throw;
        }
    }

    private bool OnResult(
        OperationPlanContext context,
        ExecutionNode node,
        ISourceSchemaResultSink sink,
        int requestIndex,
        SourceSchemaResult result)
    {
        _configuration.OnSourceSchemaResult?.Invoke(context, node, result);
        return sink.OnResult(requestIndex, result);
    }
}
