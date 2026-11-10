using HotChocolate.Fusion.Transport;
using HotChocolate.Fusion.Transport.Serialization;
using HotChocolate.Language;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// The JSON body of a single GraphQL operation request with at most one variable set.
/// </summary>
/// <param name="query">The UTF-8 encoded operation document.</param>
/// <param name="onError">The requested error handling mode, or <see langword="null"/> to omit it.</param>
/// <param name="variables">The variable values, or an empty segment to omit them.</param>
internal readonly struct SingleOperationRequestBody(
    ReadOnlyMemory<byte> query,
    ErrorHandlingMode? onError,
    JsonSegment variables)
    : IRequestBody
{
    /// <inheritdoc />
    public void WriteTo(JsonWriter writer)
        => Utf8JsonWriterHelper.WriteOperationRequest(
            writer,
            id: null,
            query.Span,
            operationName: null,
            onError,
            variables,
            extensions: JsonSegment.Empty);
}
