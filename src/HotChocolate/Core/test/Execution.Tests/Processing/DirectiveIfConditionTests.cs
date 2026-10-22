using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Language;

namespace HotChocolate.Execution.Processing;

public class DirectiveIfConditionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_Should_ReturnDefaultValue_When_VariableIsNotProvided(bool defaultValue)
    {
        // arrange
        var variables = new TestVariables();

        // act
        var enabled = DirectiveIfCondition.IsEnabled("defer", "a", variables, defaultValue);

        // assert
        Assert.Equal(defaultValue, enabled);
    }

    [Fact]
    public void IsEnabled_Should_Throw_When_RequiredVariableIsNotProvided()
    {
        // arrange
        var variables = new TestVariables();

        // act
        var exception = Assert.Throws<GraphQLException>(
            () => DirectiveIfCondition.IsEnabled("skip", "a", variables));

        // assert
        Assert.Equal(
            "The variable `a` was not provided, but the `if` argument of the `@skip` directive requires a value.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsEnabled_Should_Throw_When_VariableIsNullAndHasDefaultValue()
    {
        // arrange
        var variables = new TestVariables { ["a"] = NullValueNode.Default };

        // act
        var exception = Assert.Throws<GraphQLException>(
            () => DirectiveIfCondition.IsEnabled("defer", "a", variables, defaultValue: true));

        // assert
        Assert.Equal(
            "The variable `a` is null, but the `if` argument of the `@defer` directive must not be null.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsEnabled_Should_Throw_When_RequiredVariableIsNull()
    {
        // arrange
        var variables = new TestVariables { ["a"] = NullValueNode.Default };

        // act
        var exception = Assert.Throws<GraphQLException>(
            () => DirectiveIfCondition.IsEnabled("include", "a", variables));

        // assert
        Assert.Equal(
            "The variable `a` is null, but the `if` argument of the `@include` directive must not be null.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsEnabled_Should_Throw_When_VariableIsNotABoolean()
    {
        // arrange
        var variables = new TestVariables { ["a"] = new StringValueNode("true") };

        // act
        var withDefault = Assert.Throws<GraphQLException>(
            () => DirectiveIfCondition.IsEnabled("stream", "a", variables, defaultValue: true));
        var required = Assert.Throws<GraphQLException>(
            () => DirectiveIfCondition.IsEnabled("stream", "a", variables));

        // assert
        Assert.Equal(
            "The variable `a` has a value that is not a Boolean, "
            + "but the `if` argument of the `@stream` directive must be a Boolean.",
            withDefault.Errors[0].Message);
        Assert.Equal(withDefault.Errors[0].Message, required.Errors[0].Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_Should_ReturnVariableValue_When_VariableIsProvided(bool value)
    {
        // arrange
        var variables = new TestVariables { ["a"] = new BooleanValueNode(value) };

        // act
        var withDefault = DirectiveIfCondition.IsEnabled("defer", "a", variables, defaultValue: !value);
        var required = DirectiveIfCondition.IsEnabled("skip", "a", variables);

        // assert
        Assert.Equal(value, withDefault);
        Assert.Equal(value, required);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsIncluded_Should_ReturnNegatedSkip_When_SkipVariableIsProvided(bool skip, bool included)
    {
        // arrange
        var variables = new TestVariables { ["a"] = new BooleanValueNode(skip) };
        var condition = new IncludeCondition(skip: "a", include: null);

        // act
        var result = condition.IsIncluded(variables);

        // assert
        Assert.Equal(included, result);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IsIncluded_Should_ReturnInclude_When_IncludeVariableIsProvided(bool include, bool included)
    {
        // arrange
        var variables = new TestVariables { ["a"] = new BooleanValueNode(include) };
        var condition = new IncludeCondition(skip: null, include: "a");

        // act
        var result = condition.IsIncluded(variables);

        // assert
        Assert.Equal(included, result);
    }

    [Fact]
    public void IsIncluded_Should_Throw_When_SkipVariableIsNotProvided()
    {
        // arrange
        var condition = new IncludeCondition(skip: "a", include: null);

        // act
        var exception = Assert.Throws<GraphQLException>(() => condition.IsIncluded(new TestVariables()));

        // assert
        Assert.Equal(
            "The variable `a` was not provided, but the `if` argument of the `@skip` directive requires a value.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsIncluded_Should_Throw_When_IncludeVariableIsNotProvided()
    {
        // arrange
        var condition = new IncludeCondition(skip: null, include: "a");

        // act
        var exception = Assert.Throws<GraphQLException>(() => condition.IsIncluded(new TestVariables()));

        // assert
        Assert.Equal(
            "The variable `a` was not provided, but the `if` argument of the `@include` directive requires a value.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsIncluded_Should_Throw_When_SkipVariableIsNull()
    {
        // arrange
        var variables = new TestVariables { ["a"] = NullValueNode.Default };
        var condition = new IncludeCondition(skip: "a", include: null);

        // act
        var exception = Assert.Throws<GraphQLException>(() => condition.IsIncluded(variables));

        // assert
        Assert.Equal(
            "The variable `a` is null, but the `if` argument of the `@skip` directive must not be null.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsIncluded_Should_Throw_When_IncludeVariableIsNull()
    {
        // arrange
        var variables = new TestVariables { ["a"] = NullValueNode.Default };
        var condition = new IncludeCondition(skip: null, include: "a");

        // act
        var exception = Assert.Throws<GraphQLException>(() => condition.IsIncluded(variables));

        // assert
        Assert.Equal(
            "The variable `a` is null, but the `if` argument of the `@include` directive must not be null.",
            exception.Errors[0].Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsDeferred_Should_ReturnVariableValue_When_VariableIsProvided(bool value)
    {
        // arrange
        var variables = new TestVariables { ["a"] = new BooleanValueNode(value) };
        var condition = new DeferCondition("a");

        // act
        var deferred = condition.IsDeferred(variables);

        // assert
        Assert.Equal(value, deferred);
    }

    [Fact]
    public void IsDeferred_Should_ReturnTrue_When_VariableIsNotProvided()
    {
        // arrange
        var condition = new DeferCondition("a");

        // act
        var deferred = condition.IsDeferred(new TestVariables());

        // assert
        Assert.True(deferred);
    }

    [Fact]
    public void IsDeferred_Should_Throw_When_VariableIsNull()
    {
        // arrange
        var variables = new TestVariables { ["a"] = NullValueNode.Default };
        var condition = new DeferCondition("a");

        // act
        var exception = Assert.Throws<GraphQLException>(() => condition.IsDeferred(variables));

        // assert
        Assert.Equal(
            "The variable `a` is null, but the `if` argument of the `@defer` directive must not be null.",
            exception.Errors[0].Message);
    }

    private sealed class TestVariables : IVariableValueCollection
    {
        private readonly Dictionary<string, IValueNode> _values = [];

        public IValueNode this[string name]
        {
            set => _values[name] = value;
        }

        public bool IsEmpty => _values.Count == 0;

        public T GetValue<T>(string name) where T : IValueNode
            => TryGetValue<T>(name, out var value) ? value : throw new KeyNotFoundException(name);

        public bool TryGetValue<T>(string name, [NotNullWhen(true)] out T? value) where T : IValueNode
        {
            if (_values.TryGetValue(name, out var node) && node is T casted)
            {
                value = casted;
                return true;
            }

            value = default;
            return false;
        }

        public IEnumerator<Execution.VariableValue> GetEnumerator()
            => throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
