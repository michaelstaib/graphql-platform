using HotChocolate.Execution;
using HotChocolate.Execution.Pipeline;
using HotChocolate.Fusion.Execution.Caching;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Fusion.Planning;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Pipeline;

internal sealed class OperationDocumentNormalizer : IOperationDocumentNormalizer
{
    private readonly DocumentRewriter _documentRewriter;
    private readonly DocumentRewriter? _keepDeferRewriter;
    private readonly NormalizedDocumentCache _normalizedDocumentCache;

    public OperationDocumentNormalizer(
        FusionSchemaDefinition schema,
        NormalizedDocumentCache normalizedDocumentCache)
        : this(schema, normalizedDocumentCache, enableSinglePlanDefer: false)
    {
    }

    public OperationDocumentNormalizer(
        FusionSchemaDefinition schema,
        NormalizedDocumentCache normalizedDocumentCache,
        bool enableSinglePlanDefer)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(normalizedDocumentCache);

        _documentRewriter = new DocumentRewriter(
            schema,
            mergeAcrossDefer: true,
            removeStaticallyExcludedSelections: true);
        _keepDeferRewriter = enableSinglePlanDefer
            ? new DocumentRewriter(
                schema,
                mergeAcrossDefer: false,
                removeStaticallyExcludedSelections: true)
            : null;
        _normalizedDocumentCache = normalizedDocumentCache;
    }

    public DocumentNode NormalizeDocument(RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var documentInfo = context.OperationDocumentInfo;
        var document = documentInfo.Document
            ?? throw ThrowHelper.OperationDocumentNotAvailable();

        var operationId = context.GetOperationId();

        if (_normalizedDocumentCache.TryGet(operationId, out var normalizedDocument))
        {
            return normalizedDocument;
        }

        // Inline fragments and remove statically excluded selections before planning.
        var operationName = context.Request.OperationName;
        var rewriter = _keepDeferRewriter is not null
            && document.GetOperation(operationName).Operation is not OperationType.Subscription
                ? _keepDeferRewriter
                : _documentRewriter;
        normalizedDocument = rewriter.RewriteDocument(document, operationName);
        _normalizedDocumentCache.TryAdd(operationId, normalizedDocument);

        return normalizedDocument;
    }
}
