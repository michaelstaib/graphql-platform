using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Language;

namespace HotChocolate.Execution.Processing;

public class StreamArgumentsTests
{
    [Theory]
    [InlineData("nodes @stream")]
    [InlineData("nodes @stream(initialCount: 1)")]
    [InlineData("nodes @stream(if: true)")]
    public void IsEnabled_Should_ReturnTrue_When_IfIsMissingOrTheLiteralTrue(string field)
    {
        // arrange
        var arguments = Create(field);

        // act
        var enabled = arguments!.IsEnabled(new TestVariables());

        // assert
        Assert.True(enabled);
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("nodes @stream(if: false)")]
    [InlineData("nodes @stream(if: false, initialCount: 1)")]
    public void Create_Should_ReturnNull_When_StreamIsMissingOrDisabledByLiteral(string field)
    {
        // act
        var arguments = Create(field);

        // assert
        Assert.Null(arguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_Should_ReturnVariableValue_When_VariableIsProvided(bool value)
    {
        // arrange
        var arguments = Create("nodes @stream(if: $s)");

        // act
        var enabled = arguments!.IsEnabled(new TestVariables(new BooleanValueNode(value)));

        // assert
        Assert.Equal(value, enabled);
    }

    [Fact]
    public void IsEnabled_Should_ReturnTrue_When_VariableIsNotProvided()
    {
        // arrange
        var arguments = Create("nodes @stream(if: $s)");

        // act
        var enabled = arguments!.IsEnabled(new TestVariables());

        // assert
        Assert.True(enabled);
    }

    [Fact]
    public void IsEnabled_Should_Throw_When_VariableIsNull()
    {
        // arrange
        var arguments = Create("nodes @stream(if: $s)");

        // act
        var exception = Assert.Throws<GraphQLException>(
            () => arguments!.IsEnabled(new TestVariables(NullValueNode.Default)));

        // assert
        Assert.Equal(
            "The variable `s` is null, but the `if` argument of the `@stream` directive must not be null.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void IsEnabled_Should_Throw_When_IfIsNotABoolean()
    {
        // arrange
        var arguments = Create("nodes @stream(if: \"yes\")");

        // act
        var exception = Assert.Throws<GraphQLException>(() => arguments!.IsEnabled(new TestVariables()));

        // assert
        Assert.Equal(
            "The `if` argument of the `@stream` directive must be a Boolean.",
            exception.Errors[0].Message);
    }

    [Fact]
    public void GetInitialCountAndLabel_Should_ResolveLiterals_When_ArgumentsAreNotVariables()
    {
        // arrange
        var arguments = Create("nodes @stream(initialCount: 3, label: \"page\")");

        // act
        var usage = $"{arguments!.GetInitialCount(new TestVariables())} {arguments.GetLabel(new TestVariables())}";

        // assert
        usage.MatchInlineSnapshot("3 page");
    }

    [Fact]
    public void GetInitialCount_Should_ResolveVariable_When_InitialCountIsAVariable()
    {
        // arrange
        var arguments = Create("nodes @stream(initialCount: $count)");

        // act
        var initialCount = arguments!.GetInitialCount(new TestVariables(new IntValueNode(2)));

        // assert
        Assert.Equal(2, initialCount);
    }

    private static StreamArguments? Create(string field)
        => StreamArguments.Create(
            [new FieldSelectionNode(Utf8GraphQLParser.Syntax.ParseField(field), new ConditionFlags(0))]);

    private sealed class TestVariables(IValueNode? value = null) : IVariableValueCollection
    {
        public bool IsEmpty => value is null;

        public T GetValue<T>(string name) where T : IValueNode
            => TryGetValue<T>(name, out var result) ? result : throw new KeyNotFoundException(name);

        public bool TryGetValue<T>(string name, [NotNullWhen(true)] out T? result) where T : IValueNode
        {
            if (value is T casted)
            {
                result = casted;
                return true;
            }

            result = default;
            return false;
        }

        public IEnumerator<HotChocolate.Execution.VariableValue> GetEnumerator()
            => throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
