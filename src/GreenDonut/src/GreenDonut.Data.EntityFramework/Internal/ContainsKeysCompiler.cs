using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;

namespace GreenDonut.Data.Internal;

/// <summary>
/// Evaluates a <c>Contains</c> call's in-memory collection operand, compiling and caching one
/// delegate per expression shape. Closure instances and captured collections in the operand are
/// passed to the delegate as arguments rather than baked into it.
/// </summary>
internal static class ContainsKeysCompiler<TKey>
{
    private const int MaxCachedShapes = 256;

    private static readonly ConcurrentDictionary<string, Func<object?[], IEnumerable<TKey>>> s_cache = new();

    public static IEnumerable<TKey> Evaluate(Expression collectionExpr)
    {
        var visitor = new ShapeVisitor();
        var rewritten = visitor.Visit(collectionExpr);

        if (!visitor.IsSupported)
        {
            var operand = collectionExpr.Type == typeof(IEnumerable<TKey>)
                ? collectionExpr
                : Expression.Convert(collectionExpr, typeof(IEnumerable<TKey>));

            return Expression.Lambda<Func<IEnumerable<TKey>>>(operand).Compile()();
        }

        var shape = visitor.Shape;

        if (!s_cache.TryGetValue(shape, out var compiled))
        {
            var body = rewritten.Type == typeof(IEnumerable<TKey>)
                ? rewritten
                : Expression.Convert(rewritten, typeof(IEnumerable<TKey>));

            compiled = Expression.Lambda<Func<object?[], IEnumerable<TKey>>>(body, visitor.SlotsParameter).Compile();

            if (s_cache.Count >= MaxCachedShapes)
            {
                s_cache.Clear();
            }

            s_cache[shape] = compiled;
        }

        return compiled([.. visitor.Slots]);
    }

    // Builds a shape key over a collection operand while rewriting every closure or captured
    // collection value into a read from a delegate parameter. An unrecognized node kind marks
    // the walk unsupported.
    private sealed class ShapeVisitor
    {
        private readonly StringBuilder _shape = new();
        private readonly List<object?> _slots = [];

        public ParameterExpression SlotsParameter { get; } = Expression.Parameter(typeof(object?[]), "slots");

        public IReadOnlyList<object?> Slots => _slots;

        public bool IsSupported { get; private set; } = true;

        public string Shape => _shape.ToString();

        public Expression Visit(Expression node)
        {
            if (!IsSupported)
            {
                return node;
            }

            switch (node)
            {
                case ConstantExpression constant:
                    return VisitConstant(constant);

                case MemberExpression { Expression: not null } member:
                    return VisitMember(member);

                case MethodCallExpression call:
                    return VisitMethodCall(call);

                case UnaryExpression unary:
                    return VisitUnary(unary);

                case NewArrayExpression array:
                    return VisitNewArray(array);

                default:
                    IsSupported = false;
                    return node;
            }
        }

        private Expression VisitConstant(ConstantExpression node)
        {
            if (IsClosureSlot(node.Value, node.Type))
            {
                var index = _slots.Count;
                _slots.Add(node.Value);
                _shape.Append("K<").Append(node.Type.FullName).Append(">;");

                return Expression.Convert(
                    Expression.ArrayIndex(SlotsParameter, Expression.Constant(index)),
                    node.Type);
            }

            _shape.Append("C<").Append(node.Type.FullName).Append(">=");
            AppendLengthPrefixed(FormatLiteral(node.Value));
            _shape.Append(';');
            return node;
        }

        private Expression VisitMember(MemberExpression node)
        {
            _shape.Append("M<")
                .Append(node.Member.DeclaringType?.FullName)
                .Append('.')
                .Append(node.Member.Name)
                .Append(">;");

            var inner = Visit(node.Expression!);
            return IsSupported ? node.Update(inner) : node;
        }

        private Expression VisitMethodCall(MethodCallExpression node)
        {
            _shape.Append("F<").Append(node.Method.DeclaringType?.FullName).Append('.').Append(node.Method.Name);

            if (node.Method.IsGenericMethod)
            {
                foreach (var typeArgument in node.Method.GetGenericArguments())
                {
                    _shape.Append('|').Append(typeArgument.FullName);
                }
            }

            _shape.Append(">;");

            var instance = node.Object is null ? null : Visit(node.Object);
            var arguments = new Expression[node.Arguments.Count];

            for (var i = 0; i < arguments.Length && IsSupported; i++)
            {
                arguments[i] = Visit(node.Arguments[i]);
            }

            return IsSupported ? node.Update(instance, arguments) : node;
        }

        private Expression VisitUnary(UnaryExpression node)
        {
            _shape.Append("U<").Append((int)node.NodeType).Append('|').Append(node.Type.FullName).Append(">;");

            var operand = Visit(node.Operand);
            return IsSupported ? node.Update(operand) : node;
        }

        private Expression VisitNewArray(NewArrayExpression node)
        {
            _shape.Append("A<")
                .Append((int)node.NodeType)
                .Append('|')
                .Append(node.Type.FullName)
                .Append('|')
                .Append(node.Expressions.Count)
                .Append(">;");

            var expressions = new Expression[node.Expressions.Count];

            for (var i = 0; i < expressions.Length && IsSupported; i++)
            {
                expressions[i] = Visit(node.Expressions[i]);
            }

            return IsSupported ? node.Update(expressions) : node;
        }

        private static bool IsClosureSlot(object? value, Type type)
        {
            if (value is null)
            {
                return false;
            }

            if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                return true;
            }

            return value is IEnumerable and not string;
        }

        // The length prefix keeps a literal's rendered text from aliasing the shape key's own
        // delimiters.
        private void AppendLengthPrefixed(string text)
            => _shape.Append(text.Length).Append(':').Append(text);

        private static string FormatLiteral(object? value)
            => value switch
            {
                null => "null",
                string s => s,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
    }
}
