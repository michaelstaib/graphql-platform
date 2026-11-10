using System.Collections.Immutable;
using HotChocolate.Fusion.Transport;
using HotChocolate.Fusion.Transport.Serialization;
using HotChocolate.Language;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// The JSON body of a variable batch request that executes one operation once per variable set.
/// </summary>
/// <param name="query">The UTF-8 encoded operation document.</param>
/// <param name="onError">The requested error handling mode, or <see langword="null"/> to omit it.</param>
/// <param name="variables">The variable sets of the operation.</param>
internal readonly struct VariableBatchRequestBody(
    ReadOnlyMemory<byte> query,
    ErrorHandlingMode? onError,
    ImmutableArray<VariableValues> variables)
    : IRequestBody
{
    /// <inheritdoc />
    public void WriteTo(JsonWriter writer)
        => Utf8JsonWriterHelper.WriteVariableBatchRequest(
            writer,
            id: null,
            query.Span,
            operationName: null,
            onError,
            variables,
            extensions: JsonSegment.Empty);
}
