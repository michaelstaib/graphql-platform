using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Pipeline;

/// <summary>
/// The operation of a request after the literal values of its field arguments were replaced
/// by variables.
/// </summary>
internal sealed class ArgumentLiteralExtraction
{
    public ArgumentLiteralExtraction(
        string operationId,
        string operationHash,
        OperationDefinitionNode operation,
        ImmutableArray<VariableValue> values)
    {
        OperationId = operationId;
        OperationHash = operationHash;
        Operation = operation;
        Values = values;
    }

    /// <summary>
    /// Gets the id that all operations with this <see cref="Operation"/> share.
    /// </summary>
    public string OperationId { get; }

    /// <summary>
    /// Gets the hash of <see cref="Operation"/>.
    /// </summary>
    public string OperationHash { get; }

    /// <summary>
    /// Gets the normalized operation with variables in place of the argument literals.
    /// </summary>
    public OperationDefinitionNode Operation { get; }

    /// <summary>
    /// Gets the values of the variables that replaced the argument literals.
    /// </summary>
    public ImmutableArray<VariableValue> Values { get; }
}
