using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace GreenDonut.Data.Internal;

/// <summary>
/// Evaluates the in-memory collection operand of a <c>Contains</c> call.
/// </summary>
internal static class ContainsKeysCompiler<TKey>
{
    private const int MaxCachedShapes = 256;

    private static readonly ConcurrentDictionary<ShapeKey, Func<object?[], IEnumerable<TKey>>> s_cache =
        new(ShapeKeyComparer.Instance);

    public static IEnumerable<TKey> Evaluate(Expression collectionExpr)
    {
        var hash = new HashCode();
        var slotCount = 0;

        if (!TryHash(collectionExpr, ref hash, ref slotCount))
        {
            var operand = collectionExpr.Type == typeof(IEnumerable<TKey>)
                ? collectionExpr
                : Expression.Convert(collectionExpr, typeof(IEnumerable<TKey>));

            return Expression.Lambda<Func<IEnumerable<TKey>>>(operand).Compile()();
        }

        var lookupKey = new ShapeKey(collectionExpr, hash.ToHashCode());

        if (!s_cache.TryGetValue(lookupKey, out var compiled))
        {
            compiled = CompileAndCache(collectionExpr, lookupKey.Hash);
        }

        var slots = new object?[slotCount];
        FillSlots(collectionExpr, slots, 0);

        return compiled(slots);
    }

    private static Func<object?[], IEnumerable<TKey>> CompileAndCache(Expression collectionExpr, int hash)
    {
        var slotsParameter = Expression.Parameter(typeof(object?[]), "slots");
        var slotIndex = 0;
        var (rewritten, template) = Rewrite(collectionExpr, slotsParameter, ref slotIndex);

        var body = rewritten.Type == typeof(IEnumerable<TKey>)
            ? rewritten
            : Expression.Convert(rewritten, typeof(IEnumerable<TKey>));

        var compiled = Expression.Lambda<Func<object?[], IEnumerable<TKey>>>(body, slotsParameter).Compile();

        if (s_cache.Count >= MaxCachedShapes)
        {
            s_cache.Clear();
        }

        return s_cache.GetOrAdd(new ShapeKey(template, hash), compiled);
    }

    private static bool TryHash(Expression node, ref HashCode hash, ref int slotCount)
    {
        switch (node)
        {
            case ConstantExpression constant when IsClosureSlot(constant.Value, constant.Type):
                hash.Add(constant.Type);
                slotCount++;
                return true;

            case ConstantExpression constant:
                hash.Add(constant.Type);
                hash.Add(constant.Value);
                return true;

            case MemberExpression { Expression: not null } member:
                hash.Add(member.Member);
                return TryHash(member.Expression, ref hash, ref slotCount);

            case MethodCallExpression call:
                hash.Add(call.Method);
                hash.Add(call.Arguments.Count);
                hash.Add(call.Object is null);

                if (call.Object is not null && !TryHash(call.Object, ref hash, ref slotCount))
                {
                    return false;
                }

                foreach (var argument in call.Arguments)
                {
                    if (!TryHash(argument, ref hash, ref slotCount))
                    {
                        return false;
                    }
                }

                return true;

            case UnaryExpression unary:
                hash.Add((int)unary.NodeType);
                hash.Add(unary.Type);
                hash.Add(unary.Method);
                return TryHash(unary.Operand, ref hash, ref slotCount);

            case NewArrayExpression array:
                hash.Add((int)array.NodeType);
                hash.Add(array.Type);
                hash.Add(array.Expressions.Count);

                foreach (var element in array.Expressions)
                {
                    if (!TryHash(element, ref hash, ref slotCount))
                    {
                        return false;
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private static int FillSlots(Expression node, object?[] slots, int index)
    {
        switch (node)
        {
            case ConstantExpression constant when IsClosureSlot(constant.Value, constant.Type):
                slots[index] = constant.Value;
                return index + 1;

            case MemberExpression { Expression: not null } member:
                return FillSlots(member.Expression, slots, index);

            case MethodCallExpression call:
                if (call.Object is not null)
                {
                    index = FillSlots(call.Object, slots, index);
                }

                foreach (var argument in call.Arguments)
                {
                    index = FillSlots(argument, slots, index);
                }

                return index;

            case UnaryExpression unary:
                return FillSlots(unary.Operand, slots, index);

            case NewArrayExpression array:
                foreach (var element in array.Expressions)
                {
                    index = FillSlots(element, slots, index);
                }

                return index;

            default:
                return index;
        }
    }

    private static (Expression Rewritten, Expression Template) Rewrite(
        Expression node,
        ParameterExpression slotsParameter,
        ref int slotIndex)
    {
        switch (node)
        {
            case ConstantExpression constant when IsClosureSlot(constant.Value, constant.Type):
                var index = slotIndex++;
                var rewritten = Expression.Convert(
                    Expression.ArrayIndex(slotsParameter, Expression.Constant(index)),
                    constant.Type);

                return (rewritten, Expression.Default(constant.Type));

            case ConstantExpression constant:
                return (constant, constant);

            case MemberExpression { Expression: not null } member:
                var (memberRewritten, memberTemplate) = Rewrite(member.Expression, slotsParameter, ref slotIndex);
                return (member.Update(memberRewritten), member.Update(memberTemplate));

            case MethodCallExpression call:
                Expression? objectRewritten = null;
                Expression? objectTemplate = null;

                if (call.Object is not null)
                {
                    (objectRewritten, objectTemplate) = Rewrite(call.Object, slotsParameter, ref slotIndex);
                }

                var argumentsRewritten = new Expression[call.Arguments.Count];
                var argumentsTemplate = new Expression[call.Arguments.Count];

                for (var i = 0; i < call.Arguments.Count; i++)
                {
                    (argumentsRewritten[i], argumentsTemplate[i]) =
                        Rewrite(call.Arguments[i], slotsParameter, ref slotIndex);
                }

                return (call.Update(objectRewritten, argumentsRewritten), call.Update(objectTemplate, argumentsTemplate));

            case UnaryExpression unary:
                var (unaryRewritten, unaryTemplate) = Rewrite(unary.Operand, slotsParameter, ref slotIndex);
                return (unary.Update(unaryRewritten), unary.Update(unaryTemplate));

            case NewArrayExpression array:
                var elementsRewritten = new Expression[array.Expressions.Count];
                var elementsTemplate = new Expression[array.Expressions.Count];

                for (var i = 0; i < array.Expressions.Count; i++)
                {
                    (elementsRewritten[i], elementsTemplate[i]) =
                        Rewrite(array.Expressions[i], slotsParameter, ref slotIndex);
                }

                return (array.Update(elementsRewritten), array.Update(elementsTemplate));

            default:
                return (node, node);
        }
    }

    private static bool IsClosureSlot(object? value, Type type)
        => value is not null && !IsLiteralType(type);

    private static bool IsLiteralType(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);

    private readonly struct ShapeKey(Expression template, int hash)
    {
        public Expression Template { get; } = template;

        public int Hash { get; } = hash;
    }

    private sealed class ShapeKeyComparer : IEqualityComparer<ShapeKey>
    {
        public static readonly ShapeKeyComparer Instance = new();

        public bool Equals(ShapeKey x, ShapeKey y)
            => StructurallyEqual(x.Template, y.Template);

        public int GetHashCode(ShapeKey key)
            => key.Hash;

        private static bool StructurallyEqual(Expression a, Expression b)
        {
            var slotA = IsSlotNode(a);
            var slotB = IsSlotNode(b);

            if (slotA || slotB)
            {
                return slotA && slotB && ReferenceEquals(a.Type, b.Type);
            }

            if (a.NodeType != b.NodeType || !ReferenceEquals(a.Type, b.Type))
            {
                return false;
            }

            switch (a)
            {
                case ConstantExpression constantA when b is ConstantExpression constantB:
                    return Equals(constantA.Value, constantB.Value);

                case MemberExpression memberA when b is MemberExpression memberB:
                    return memberA.Member.Equals(memberB.Member)
                        && StructurallyEqual(memberA.Expression!, memberB.Expression!);

                case MethodCallExpression callA when b is MethodCallExpression callB:
                    return MethodCallsEqual(callA, callB);

                case UnaryExpression unaryA when b is UnaryExpression unaryB:
                    return Equals(unaryA.Method, unaryB.Method)
                        && StructurallyEqual(unaryA.Operand, unaryB.Operand);

                case NewArrayExpression arrayA when b is NewArrayExpression arrayB:
                    return NewArraysEqual(arrayA, arrayB);

                default:
                    return false;
            }
        }

        private static bool MethodCallsEqual(MethodCallExpression a, MethodCallExpression b)
        {
            if (!a.Method.Equals(b.Method)
                || a.Arguments.Count != b.Arguments.Count
                || (a.Object is null) != (b.Object is null))
            {
                return false;
            }

            if (a.Object is not null && !StructurallyEqual(a.Object, b.Object!))
            {
                return false;
            }

            for (var i = 0; i < a.Arguments.Count; i++)
            {
                if (!StructurallyEqual(a.Arguments[i], b.Arguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NewArraysEqual(NewArrayExpression a, NewArrayExpression b)
        {
            if (a.Expressions.Count != b.Expressions.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Expressions.Count; i++)
            {
                if (!StructurallyEqual(a.Expressions[i], b.Expressions[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSlotNode(Expression node)
            => node.NodeType == ExpressionType.Default
                || (node is ConstantExpression constant && IsClosureSlot(constant.Value, constant.Type));
    }
}
