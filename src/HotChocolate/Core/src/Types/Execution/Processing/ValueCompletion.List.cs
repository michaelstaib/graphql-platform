using System.Buffers;
using System.Collections;
using System.Text.Json;
using HotChocolate.Text.Json;
using HotChocolate.Types;
using static HotChocolate.Execution.ErrorHelper;

namespace HotChocolate.Execution.Processing;

internal static partial class ValueCompletion
{
    private static void CompleteListValue(
        ValueCompletionContext context,
        Selection selection,
        IType type,
        ResultElement resultValue,
        object runtimeValue)
    {
        var elementType = type.InnerType();

        if (runtimeValue is Array array)
        {
            var i = 0;

            resultValue.SetArrayValue(array.Length);

            foreach (var element in resultValue.EnumerateArray())
            {
                if (!TryCompleteListItem(
                    context,
                    selection,
                    elementType,
                    element,
                    array.GetValue(i++)))
                {
                    return;
                }
            }

            return;
        }

        if (runtimeValue is IList list)
        {
            var i = 0;

            resultValue.SetArrayValue(list.Count);

            foreach (var element in resultValue.EnumerateArray())
            {
                if (!TryCompleteListItem(
                    context,
                    selection,
                    elementType,
                    element,
                    list[i++]))
                {
                    return;
                }
            }

            return;
        }

        if (runtimeValue is JsonElement { ValueKind: JsonValueKind.Array } node)
        {
            resultValue.SetArrayValue(node.GetArrayLength());

            using var runtimeEnumerator = node.EnumerateArray().GetEnumerator();

            foreach (var element in resultValue.EnumerateArray())
            {
                runtimeEnumerator.MoveNext();

                if (!TryCompleteListItem(
                    context,
                    selection,
                    elementType,
                    element,
                    runtimeEnumerator.Current))
                {
                    return;
                }
            }

            return;
        }

        if (runtimeValue is IEnumerable enumerable)
        {
            var count = 0;
            var buffer = ArrayPool<object?>.Shared.Rent(64);

            try
            {
                foreach (var value in enumerable)
                {
                    if (count == buffer.Length)
                    {
                        var newBuffer = ArrayPool<object?>.Shared.Rent(buffer.Length * 2);
                        var span = buffer.AsSpan(0, count);
                        span.CopyTo(newBuffer);
                        span.Clear();
                        ArrayPool<object?>.Shared.Return(buffer);
                        buffer = newBuffer;
                    }

                    buffer[count++] = value;
                }

                resultValue.SetArrayValue(count);

                var i = 0;

                foreach (var element in resultValue.EnumerateArray())
                {
                    if (!TryCompleteListItem(
                        context,
                        selection,
                        elementType,
                        element,
                        buffer[i++]))
                    {
                        return;
                    }
                }
            }
            finally
            {
                buffer.AsSpan(0, count).Clear();
                ArrayPool<object?>.Shared.Return(buffer);
            }

            return;
        }

        var operationContext = context.OperationContext;
        var resolverContext = context.ResolverContext;
        var error = ListValueIsNotSupported(runtimeValue.GetType(), resultValue.Path);
        operationContext.ReportError(error, resolverContext);
    }

    private static bool TryCompleteListItem(
        ValueCompletionContext context,
        Selection selection,
        IType elementType,
        ResultElement element,
        object? runtimeItem)
    {
        Complete(context, selection, elementType, element, runtimeItem);

        // if we ran into an error that invalidated the result we abort.
        if (element.IsInvalidated)
        {
            return false;
        }

        if (elementType.Kind is not TypeKind.NonNull || !element.IsNullOrInvalidated)
        {
            return true;
        }

        var operationContext = context.OperationContext;
        var path = element.Path;

        if (!operationContext.PropagateNullValues)
        {
            element.SetNullValue();
            operationContext.Result.AddNonNullViolation(path);
            return true;
        }

        PropagateNullValues(element);
        operationContext.Result.AddNonNullViolation(path);
        return false;
    }

    internal static void PropagateNullValues(ResultElement result)
    {
        result.SetNullValue();

        do
        {
            result = result.Parent;

            if (result.IsNullable)
            {
                result.SetNullValue();
                return;
            }

            result.Invalidate();
        } while (result.Parent is { ValueKind: not JsonValueKind.Undefined, IsInvalidated: false });
    }
}
