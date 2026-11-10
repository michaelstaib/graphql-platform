using System.Collections.Immutable;
using HotChocolate.Fusion.Transport;
using HotChocolate.Fusion.Transport.Serialization;
using HotChocolate.Language;
using HotChocolate.Text.Json;

namespace HotChocolate.Fusion.Execution.Clients;

/// <summary>
/// The JSON body of an operation batch built from the requests of one batch. A batch that
/// resolves to a single entry is written as that entry instead of a JSON array.
/// </summary>
/// <param name="requests">The requests of the batch, at least one.</param>
/// <param name="onError">The requested error handling mode, or <see langword="null"/> to omit it.</param>
/// <param name="supportsVariableBatching">
/// <see langword="true"/> to send the variable sets of a request as one variable batch entry,
/// <see langword="false"/> to send one entry per variable set.
/// </param>
internal readonly struct OperationBatchRequestBody(
    ImmutableArray<SourceSchemaClientRequest> requests,
    ErrorHandlingMode? onError,
    bool supportsVariableBatching)
    : IRequestBody
{
    /// <inheritdoc />
    public void WriteTo(JsonWriter writer)
    {
        if (CountEntries() == 1)
        {
            WriteEntries(writer, in requests.ItemRef(0));
            return;
        }

        writer.WriteStartArray();

        for (var i = 0; i < requests.Length; i++)
        {
            WriteEntries(writer, in requests.ItemRef(i));
        }

        writer.WriteEndArray();
    }

    private int CountEntries()
    {
        if (supportsVariableBatching)
        {
            return requests.Length;
        }

        var count = 0;

        for (var i = 0; i < requests.Length; i++)
        {
            var variableSetCount = requests.ItemRef(i).Variables.Length;
            count += variableSetCount > 1 ? variableSetCount : 1;
        }

        return count;
    }

    private void WriteEntries(JsonWriter writer, in SourceSchemaClientRequest request)
    {
        var query = request.OperationSourceText.Value.Span;
        var variables = request.Variables;

        switch (variables.Length)
        {
            case 0:
                Utf8JsonWriterHelper.WriteOperationRequest(
                    writer,
                    id: null,
                    query,
                    operationName: null,
                    onError,
                    JsonSegment.Empty,
                    extensions: JsonSegment.Empty);
                break;

            case 1:
                Utf8JsonWriterHelper.WriteOperationRequest(
                    writer,
                    id: null,
                    query,
                    operationName: null,
                    onError,
                    variables[0].Values,
                    extensions: JsonSegment.Empty);
                break;

            default:
                if (supportsVariableBatching)
                {
                    Utf8JsonWriterHelper.WriteVariableBatchRequest(
                        writer,
                        id: null,
                        query,
                        operationName: null,
                        onError,
                        variables,
                        extensions: JsonSegment.Empty);
                }
                else
                {
                    foreach (var variableSet in variables)
                    {
                        Utf8JsonWriterHelper.WriteOperationRequest(
                            writer,
                            id: null,
                            query,
                            operationName: null,
                            onError,
                            variableSet.Values,
                            extensions: JsonSegment.Empty);
                    }
                }

                break;
        }
    }
}
