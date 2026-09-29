using System.Linq.Expressions;
using System.Runtime.Loader;
using GreenDonut.Data.Internal;

namespace GreenDonut.Data;

public class ContainsKeysCompilerTests
{
    [Fact]
    public void Evaluate_Should_RunEachOverload_When_ParameterTypesAreAssignable()
    {
        // Arrange
        var holder = new ClosureHolder(["seed"]);
        var keysAccess = Expression.Property(Expression.Constant(holder), nameof(ClosureHolder.Keys));
        var enumerableMethod = typeof(Picker).GetMethod(nameof(Picker.Pick), [typeof(IEnumerable<string>)])!;
        var arrayMethod = typeof(Picker).GetMethod(nameof(Picker.Pick), [typeof(string[])])!;
        var enumerableCall = Expression.Call(enumerableMethod, keysAccess);
        var arrayCall = Expression.Call(arrayMethod, keysAccess);

        // Act
        var enumerableResult = ContainsKeysCompiler<string>.Evaluate(enumerableCall).ToArray();
        var arrayResult = ContainsKeysCompiler<string>.Evaluate(arrayCall).ToArray();

        // Assert
        Assert.Equal(["enumerable-overload"], enumerableResult);
        Assert.Equal(["array-overload"], arrayResult);
    }

    [Fact]
    public void Evaluate_Should_MissTheCache_When_OnlyTheTypeNameMatches()
    {
        // Arrange
        var defaultType = typeof(KeySource);
        var defaultHolder = (KeySource)Activator.CreateInstance(defaultType)!;
        defaultHolder.Keys = ["skipped", "default-context"];
        var defaultShape = BuildSkipShape(defaultType, defaultHolder);

        var context = new AssemblyLoadContext(nameof(Evaluate_Should_MissTheCache_When_OnlyTheTypeNameMatches), isCollectible: true);

        try
        {
            var loadedAssembly = context.LoadFromAssemblyPath(defaultType.Assembly.Location);
            var loadedType = loadedAssembly.GetType(defaultType.FullName!)!;
            var loadedHolder = Activator.CreateInstance(loadedType)!;
            loadedType.GetField(nameof(KeySource.Keys))!.SetValue(loadedHolder, new[] { "skipped", "loaded-context" });
            var loadedShape = BuildSkipShape(loadedType, loadedHolder);

            // Act
            var defaultResult = ContainsKeysCompiler<string>.Evaluate(defaultShape).ToArray();
            var loadedResult = ContainsKeysCompiler<string>.Evaluate(loadedShape).ToArray();

            // Assert
            Assert.Equal(["default-context"], defaultResult);
            Assert.Equal(["loaded-context"], loadedResult);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Evaluate_Should_UseEachCallsOwnKeys_When_ShapeIsCached()
    {
        // Arrange
        var firstShape = BuildSkipOneShape(["A", "B"]);
        var secondShape = BuildSkipOneShape(["C", "D", "E"]);

        // Act
        var firstResult = ContainsKeysCompiler<string>.Evaluate(firstShape).ToArray();
        var secondResult = ContainsKeysCompiler<string>.Evaluate(secondShape).ToArray();

        // Assert
        Assert.Equal(["B"], firstResult);
        Assert.Equal(["D", "E"], secondResult);
    }

    private static Expression BuildSkipShape(Type holderType, object holder)
    {
        var fieldAccess = Expression.Field(Expression.Constant(holder, holderType), nameof(KeySource.Keys));
        var skipMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.Skip))!.MakeGenericMethod(typeof(string));

        return Expression.Call(skipMethod, fieldAccess, Expression.Constant(1));
    }

    private static Expression BuildSkipOneShape(string[] keys)
    {
        var skipMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.Skip))!.MakeGenericMethod(typeof(string));

        return Expression.Call(skipMethod, Expression.Constant(keys), Expression.Constant(1));
    }

    private sealed class ClosureHolder(string[] keys)
    {
        public string[] Keys { get; } = keys;
    }

    public sealed class KeySource
    {
        public string[] Keys = [];
    }

    private static class Picker
    {
        public static IEnumerable<string> Pick(IEnumerable<string> source) => ["enumerable-overload"];

        public static IEnumerable<string> Pick(string[] source) => ["array-overload"];
    }
}
