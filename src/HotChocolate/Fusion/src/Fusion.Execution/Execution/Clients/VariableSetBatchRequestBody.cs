using System.Collections.Immutable;
using HotChocolate.Fusion.Transport;
using HotChocolate.Fusion.Transport.Serialization;
using HotChocolate.Language;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// The JSON body of an operation batch that sends one operation request per variable set of
/// one operation, written as a JSON array.
/// </summary>
/// <param name="query">The UTF-8 encoded operation document.</param>
/// <param name="onError">The requested error handling mode, or <see langword="null"/> to omit it.</param>
/// <param name="variables">The variable sets of the operation, at least two.</param>
internal readonly struct VariableSetBatchRequestBody(
    ReadOnlyMemory<byte> query,
    ErrorHandlingMode? onError,
    ImmutableArray<VariableValues> variables)
    : IRequestBody
{
    /// <inheritdoc />
    public void WriteTo(JsonWriter writer)
    {
        var querySpan = query.Span;

        writer.WriteStartArray();

        foreach (var variableSet in variables)
        {
            Utf8JsonWriterHelper.WriteOperationRequest(
                writer,
                id: null,
                querySpan,
                operationName: null,
                onError,
                variableSet.Values,
                extensions: JsonSegment.Empty);
        }

        writer.WriteEndArray();
    }
}
