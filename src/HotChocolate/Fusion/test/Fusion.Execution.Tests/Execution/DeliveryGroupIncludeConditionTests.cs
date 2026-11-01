using System.Text;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Execution;

public class DeliveryGroupIncludeConditionTests : FusionTestBase
{
    private readonly ObjectPool<OrderedDictionary<string, List<FieldSelectionNode>>> _fieldMapPool =
        new DefaultObjectPool<OrderedDictionary<string, List<FieldSelectionNode>>>(new FieldMapPooledObjectPolicy());

    [Fact]
    public void IsIncluded_Should_ReadIncludeFlags_When_ConditionComesFromDeliveryGroup()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var skip = new IncludeCondition(skip: "y", include: null);
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Conditions = [include, skip] };
        var operationDefinition = ParseOperation("query($x: Boolean!, $y: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile("1", "1", "1", operationDefinition, [deliveryGroup]);

        // act
        var variables = CreateVariables(schema, new Dictionary<string, bool> { ["x"] = true, ["y"] = true });
        var flags = operation.CreateIncludeConditionFlags(variables);

        // assert
        Assert.Equal(2, operation.IncludeConditionCount);
        Assert.True(operation.IsIncluded(include, flags));
        Assert.False(operation.IsIncluded(skip, flags));
    }

    [Fact]
    public void IsIncluded_Should_ReadOverflowFlags_When_DeliveryGroupConditionIsBeyondFirstWord()
    {
        // arrange
        const int conditionCount = 64;
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Conditions = [include] };
        var sourceText = new StringBuilder();
        sourceText.Append("query($x: Boolean! ");

        for (var i = 0; i < conditionCount; i++)
        {
            sourceText.Append($"$v{i}: Boolean! ");
        }

        sourceText.Append(") { plain: foo");

        for (var i = 0; i < conditionCount; i++)
        {
            sourceText.Append($" f{i}: foo @include(if: $v{i})");
        }

        sourceText.Append(" }");

        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile(
            "1",
            "1",
            "1",
            ParseOperation(sourceText.ToString()),
            [deliveryGroup]);

        // act
        var enabled = new Dictionary<string, bool> { ["x"] = true };
        var disabled = new Dictionary<string, bool> { ["x"] = false };

        for (var i = 0; i < conditionCount; i++)
        {
            enabled[$"v{i}"] = false;
            disabled[$"v{i}"] = true;
        }

        var enabledFlags = operation.CreateIncludeConditionFlags(CreateVariables(schema, enabled));
        var disabledFlags = operation.CreateIncludeConditionFlags(CreateVariables(schema, disabled));

        // assert
        Assert.True(operation.HasWideIncludeFlags);
        Assert.True(operation.IsIncluded(include, enabledFlags));
        Assert.False(operation.IsIncluded(include, disabledFlags));
    }

    [Fact]
    public void IsIncluded_Should_Throw_When_ConditionWasNotCompiledIntoOperation()
    {
        // arrange
        var schema = CreateSchema();
        var operationDefinition = ParseOperation("query($x: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile("1", "1", "1", operationDefinition);

        // act
        var exception = Assert.Throws<InvalidOperationException>(
            () => operation.IsIncluded(new IncludeCondition(skip: null, include: "x"), new ConditionFlags(0)));

        // assert
        Assert.Equal("The include condition (skip: , include: x) is not part of the operation.", exception.Message);
    }

    private static FusionSchemaDefinition CreateSchema()
    {
        const string sourceText =
            """
            type Query {
                foo: String
            }

            scalar Boolean
            """;

        return ComposeSchema(sourceText);
    }

    private static OperationDefinitionNode ParseOperation(string sourceText)
        => Utf8GraphQLParser.Parse(sourceText)
            .Definitions
            .OfType<OperationDefinitionNode>()
            .First();

    private static VariableValueCollection CreateVariables(
        FusionSchemaDefinition schema,
        Dictionary<string, bool> values)
    {
        var booleanType = new NonNullType(schema.Types.GetType<IScalarTypeDefinition>("Boolean"));
        var variables = new Dictionary<string, VariableValue>();

        foreach (var (name, value) in values)
        {
            variables.Add(
                name,
                new VariableValue(name, booleanType, value ? BooleanValueNode.True : BooleanValueNode.False));
        }

        return new VariableValueCollection(variables);
    }
}
