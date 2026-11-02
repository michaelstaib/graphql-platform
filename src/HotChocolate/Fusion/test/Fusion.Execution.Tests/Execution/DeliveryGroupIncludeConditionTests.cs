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
    public void AreDeliveryGroupConditionsIncluded_Should_ReadFlags_When_ConditionsComeFromGroups()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var skip = new IncludeCondition(skip: "y", include: null);
        var includeGroup = new DeliveryGroup(null, null, 0) { Id = 0, Conditions = [include] };
        var skipGroup = new DeliveryGroup(null, null, 0) { Id = 1, Conditions = [skip] };
        var bothGroup = new DeliveryGroup(null, null, 0) { Id = 2, Conditions = [include, skip] };
        var operationDefinition = ParseOperation("query($x: Boolean!, $y: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile(
            "1",
            "1",
            "1",
            operationDefinition,
            [includeGroup, skipGroup, bothGroup]);

        // act
        var variables = CreateVariables(schema, new Dictionary<string, bool> { ["x"] = true, ["y"] = true });
        var flags = operation.CreateIncludeConditionFlags(variables);

        // assert
        Assert.Equal(2, operation.IncludeConditionCount);
        Assert.True(operation.AreDeliveryGroupConditionsIncluded(includeGroup, flags));
        Assert.False(operation.AreDeliveryGroupConditionsIncluded(skipGroup, flags));
        Assert.False(operation.AreDeliveryGroupConditionsIncluded(bothGroup, flags));
    }

    [Fact]
    public void AreDeliveryGroupConditionsIncluded_Should_ReadOverflow_When_ConditionIsBeyondFirstWord()
    {
        // arrange
        const int conditionCount = 64;
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Id = 0, Conditions = [include] };
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
        Assert.True(operation.AreDeliveryGroupConditionsIncluded(deliveryGroup, enabledFlags));
        Assert.False(operation.AreDeliveryGroupConditionsIncluded(deliveryGroup, disabledFlags));
    }

    [Fact]
    public void AreDeliveryGroupConditionsIncluded_Should_ReturnTrue_When_GroupIsUnconditionalAndNotCompiled()
    {
        // arrange
        var schema = CreateSchema();
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Id = 0 };
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile("1", "1", "1", ParseOperation("query { foo }"));
        var plan = OperationPlan.Create(
            "1",
            operation,
            rootNodes: [],
            allNodes: [],
            deliveryGroups: [deliveryGroup],
            incrementalPlans: [],
            searchSpace: 0,
            expandedNodes: 0);

        // act
        var flags = plan.Operation.CreateIncludeConditionFlags(CreateVariables(schema, []));
        var isIncluded = plan.Operation.AreDeliveryGroupConditionsIncluded(plan.DeliveryGroups[0], flags);

        // assert
        Assert.True(isIncluded);
    }

    [Fact]
    public void Create_Should_ThrowArgumentException_When_GroupConditionsWereNotCompiled()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Id = 0, Conditions = [include] };
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile("1", "1", "1", ParseOperation("query($x: Boolean!) { foo }"));

        // act
        var exception = Assert.Throws<ArgumentException>(
            () => OperationPlan.Create(
                "1",
                operation,
                rootNodes: [],
                allNodes: [],
                deliveryGroups: [deliveryGroup],
                incrementalPlans: [],
                searchSpace: 0,
                expandedNodes: 0));

        // assert
        Assert.Equal(
            "The include conditions of the delivery group with the ID '0' were not compiled into the operation. "
            + "(Parameter 'deliveryGroups')",
            exception.Message);
    }

    [Fact]
    public void AreDeliveryGroupConditionsIncluded_Should_ReadFlags_When_GroupHasIfVariable()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var ifGroup = new DeliveryGroup(null, null, 0) { Id = 0, IfVariable = "d" };
        var ifAndIncludeGroup = new DeliveryGroup(null, null, 0) { Id = 1, IfVariable = "d", Conditions = [include] };
        var sharedGroup = new DeliveryGroup(null, null, 0) { Id = 2, IfVariable = "x" };
        var operationDefinition = ParseOperation("query($d: Boolean!, $x: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile(
            "1",
            "1",
            "1",
            operationDefinition,
            [ifGroup, ifAndIncludeGroup, sharedGroup]);

        // act
        var enabled = operation.CreateIncludeConditionFlags(
            CreateVariables(schema, new Dictionary<string, bool> { ["d"] = true, ["x"] = true }));
        var deferDisabled = operation.CreateIncludeConditionFlags(
            CreateVariables(schema, new Dictionary<string, bool> { ["d"] = false, ["x"] = true }));
        var includeDisabled = operation.CreateIncludeConditionFlags(
            CreateVariables(schema, new Dictionary<string, bool> { ["d"] = true, ["x"] = false }));

        // assert
        Assert.Equal(2, operation.IncludeConditionCount);
        Assert.Equal(
            [true, true, true],
            [
                operation.AreDeliveryGroupConditionsIncluded(ifGroup, enabled),
                operation.AreDeliveryGroupConditionsIncluded(ifAndIncludeGroup, enabled),
                operation.AreDeliveryGroupConditionsIncluded(sharedGroup, enabled)
            ]);
        Assert.Equal(
            [false, false, true],
            [
                operation.AreDeliveryGroupConditionsIncluded(ifGroup, deferDisabled),
                operation.AreDeliveryGroupConditionsIncluded(ifAndIncludeGroup, deferDisabled),
                operation.AreDeliveryGroupConditionsIncluded(sharedGroup, deferDisabled)
            ]);
        Assert.Equal(
            [true, false, false],
            [
                operation.AreDeliveryGroupConditionsIncluded(ifGroup, includeDisabled),
                operation.AreDeliveryGroupConditionsIncluded(ifAndIncludeGroup, includeDisabled),
                operation.AreDeliveryGroupConditionsIncluded(sharedGroup, includeDisabled)
            ]);
    }

    [Fact]
    public void IsDeliveryGroupExcluded_Should_IgnoreDeferIf_When_GroupHasIfVariable()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var ifGroup = new DeliveryGroup(null, null, 0) { Id = 0, IfVariable = "d" };
        var ifAndIncludeGroup = new DeliveryGroup(null, null, 0) { Id = 1, IfVariable = "d", Conditions = [include] };
        var sharedGroup = new DeliveryGroup(null, null, 0) { Id = 2, IfVariable = "x" };
        var unconditionalGroup = new DeliveryGroup(null, null, 0) { Id = 3 };
        var operationDefinition = ParseOperation("query($d: Boolean!, $x: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile(
            "1",
            "1",
            "1",
            operationDefinition,
            [ifGroup, ifAndIncludeGroup, sharedGroup, unconditionalGroup]);

        // act
        var enabled = operation.CreateIncludeConditionFlags(
            CreateVariables(schema, new Dictionary<string, bool> { ["d"] = true, ["x"] = true }));
        var deferDisabled = operation.CreateIncludeConditionFlags(
            CreateVariables(schema, new Dictionary<string, bool> { ["d"] = false, ["x"] = true }));
        var includeDisabled = operation.CreateIncludeConditionFlags(
            CreateVariables(schema, new Dictionary<string, bool> { ["d"] = true, ["x"] = false }));

        // assert
        Assert.Equal(
            [false, false, false, false],
            [
                operation.IsDeliveryGroupExcluded(ifGroup, enabled),
                operation.IsDeliveryGroupExcluded(ifAndIncludeGroup, enabled),
                operation.IsDeliveryGroupExcluded(sharedGroup, enabled),
                operation.IsDeliveryGroupExcluded(unconditionalGroup, enabled)
            ]);
        Assert.Equal(
            [false, false, false, false],
            [
                operation.IsDeliveryGroupExcluded(ifGroup, deferDisabled),
                operation.IsDeliveryGroupExcluded(ifAndIncludeGroup, deferDisabled),
                operation.IsDeliveryGroupExcluded(sharedGroup, deferDisabled),
                operation.IsDeliveryGroupExcluded(unconditionalGroup, deferDisabled)
            ]);
        Assert.Equal(
            [false, true, false, false],
            [
                operation.IsDeliveryGroupExcluded(ifGroup, includeDisabled),
                operation.IsDeliveryGroupExcluded(ifAndIncludeGroup, includeDisabled),
                operation.IsDeliveryGroupExcluded(sharedGroup, includeDisabled),
                operation.IsDeliveryGroupExcluded(unconditionalGroup, includeDisabled)
            ]);
    }

    [Fact]
    public void Create_Should_ThrowArgumentException_When_GroupIfVariableWasNotCompiled()
    {
        // arrange
        var schema = CreateSchema();
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Id = 0, IfVariable = "d" };
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile("1", "1", "1", ParseOperation("query($d: Boolean!) { foo }"));

        // act
        var exception = Assert.Throws<ArgumentException>(
            () => OperationPlan.Create(
                "1",
                operation,
                rootNodes: [],
                allNodes: [],
                deliveryGroups: [deliveryGroup],
                incrementalPlans: [],
                searchSpace: 0,
                expandedNodes: 0));

        // assert
        Assert.Equal(
            "The include conditions of the delivery group with the ID '0' were not compiled into the operation. "
            + "(Parameter 'deliveryGroups')",
            exception.Message);
    }

    [Fact]
    public void AreDeliveryGroupConditionsIncluded_Should_ReadFlags_When_GroupIdsAreSparse()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var skip = new IncludeCondition(skip: "y", include: null);
        var unconditionalGroup = new DeliveryGroup(null, null, 0) { Id = 1 };
        var skipGroup = new DeliveryGroup(null, null, 0) { Id = int.MaxValue, Conditions = [skip] };
        var includeGroup = new DeliveryGroup(null, null, 0) { Id = 3, Conditions = [include] };
        var operationDefinition = ParseOperation("query($x: Boolean!, $y: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);
        var operation = compiler.Compile(
            "1",
            "1",
            "1",
            operationDefinition,
            [unconditionalGroup, skipGroup, includeGroup]);

        // act
        var variables = CreateVariables(schema, new Dictionary<string, bool> { ["x"] = true, ["y"] = true });
        var flags = operation.CreateIncludeConditionFlags(variables);

        // assert
        Assert.True(operation.AreDeliveryGroupConditionsIncluded(includeGroup, flags));
        Assert.False(operation.AreDeliveryGroupConditionsIncluded(skipGroup, flags));
        Assert.True(operation.AreDeliveryGroupConditionsIncluded(unconditionalGroup, flags));
    }

    [Fact]
    public void Compile_Should_ThrowArgumentException_When_GroupIdIsNegative()
    {
        // arrange
        var schema = CreateSchema();
        var include = new IncludeCondition(skip: null, include: "x");
        var deliveryGroup = new DeliveryGroup(null, null, 0) { Id = -2, Conditions = [include] };
        var operationDefinition = ParseOperation("query($x: Boolean!) { foo }");
        var compiler = new OperationCompiler(schema, _fieldMapPool);

        // act
        var exception = Assert.Throws<ArgumentException>(
            () => compiler.Compile("1", "1", "1", operationDefinition, [deliveryGroup]));

        // assert
        Assert.Equal(
            "The delivery group ID '-2' is invalid. Delivery group IDs must be zero or greater. "
            + "(Parameter 'deliveryGroups')",
            exception.Message);
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
