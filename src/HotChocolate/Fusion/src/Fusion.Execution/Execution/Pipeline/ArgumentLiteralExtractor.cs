using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using HotChocolate.Caching.Memory;
using HotChocolate.Execution;
using HotChocolate.Execution.Pipeline;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Pipeline;

/// <summary>
/// Replaces the literal values of the field arguments of a request with variables and provides
/// the id that requests share when they differ only in these values.
/// </summary>
internal sealed class ArgumentLiteralExtractor
{
    private const string OperationIdSuffix = ".literals";

    private readonly ArgumentLiteralRewriter _rewriter;
    private readonly IDocumentHashProvider _hashProvider;
    private readonly Cache<Entry> _cache;

    public ArgumentLiteralExtractor(
        ISchemaDefinition schema,
        IDocumentHashProvider hashProvider,
        int cacheSize)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(hashProvider);

        _rewriter = new ArgumentLiteralRewriter(schema);
        _hashProvider = hashProvider;
        _cache = new Cache<Entry>(cacheSize);
    }

    /// <summary>
    /// Replaces the argument literals of the operation of <paramref name="context"/> with variables,
    /// adds the values of these variables to the variable values of the request and stores the
    /// <see cref="ArgumentLiteralExtraction"/> as a feature of the request.
    /// </summary>
    /// <param name="context">
    /// The request context with a validated document.
    /// </param>
    /// <returns>
    /// The id to cache the operation plan under. The operation id of the request if no
    /// argument literal was replaced or the request has no coerced variable values.
    /// </returns>
    public string Extract(RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var operationId = context.GetOperationId();

        if (context.VariableValues.IsDefaultOrEmpty)
        {
            return operationId;
        }

        if (!_cache.TryGet(operationId, out var entry))
        {
            entry = CreateEntry(context.GetNormalizedOperation());
            _cache.TryAdd(operationId, entry);
        }

        if (entry.Extraction is not { } extraction)
        {
            return operationId;
        }

        // A request context that passes the pipeline again already holds the variable values.
        if (context.Features.Get<ArgumentLiteralExtraction>() is null)
        {
            context.Features.Set(extraction);
            AddVariableValues(context, extraction.Values);
        }

        return extraction.OperationId;
    }

    private Entry CreateEntry(OperationDefinitionNode operation)
    {
        if (!_rewriter.TryRewrite(operation, out var rewritten, out var values))
        {
            return Entry.None;
        }

        var sourceText = Encoding.UTF8.GetBytes(rewritten.ToString(indented: false));
        var hash = _hashProvider.ComputeHash(sourceText).Value;

        return new Entry(new ArgumentLiteralExtraction(hash + OperationIdSuffix, hash, rewritten, values));
    }

    private static void AddVariableValues(RequestContext context, ImmutableArray<VariableValue> values)
    {
        var variableSets = ImmutableCollectionsMarshal.AsArray(context.VariableValues).AsSpan();
        IVariableValueCollection[]? updatedVariableSets = null;

        for (var i = 0; i < variableSets.Length; i++)
        {
            if (variableSets[i] is not VariableValueCollection variableValues)
            {
                variableValues = VariableValueCollection.Copy(variableSets[i], values.Length);
                updatedVariableSets ??= [.. variableSets];
                updatedVariableSets[i] = variableValues;
            }

            foreach (var value in values)
            {
                variableValues.Add(value.Name, value);
            }
        }

        if (updatedVariableSets is not null)
        {
            context.VariableValues = ImmutableCollectionsMarshal.AsImmutableArray(updatedVariableSets);
        }
    }

    private sealed class Entry(ArgumentLiteralExtraction? extraction)
    {
        // The entry of an operation without an argument literal that can be replaced.
        public static readonly Entry None = new(null);

        public ArgumentLiteralExtraction? Extraction => extraction;
    }
}
