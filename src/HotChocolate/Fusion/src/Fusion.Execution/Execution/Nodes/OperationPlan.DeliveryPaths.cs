using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Execution.Nodes;

public sealed partial record OperationPlan
{
    private readonly Dictionary<int, DeliveryPath>? _deliveryPaths;

    internal DeliveryPath GetDeliveryPath(int deliveryGroupId)
    {
        if (_deliveryPaths is not null
            && _deliveryPaths.TryGetValue(deliveryGroupId, out var deliveryPath))
        {
            return deliveryPath;
        }

        throw ThrowHelper.DeliveryGroupNotFound(deliveryGroupId);
    }

    private static Dictionary<int, DeliveryPath>? CreateDeliveryPaths(
        Operation operation,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        ImmutableArray<IncrementalPlan> incrementalPlans)
    {
        if (deliveryGroups.IsDefaultOrEmpty)
        {
            return null;
        }

        if (incrementalPlans.IsDefault)
        {
            incrementalPlans = [];
        }

        var deliveryPaths = new Dictionary<int, DeliveryPath>(deliveryGroups.Length);

        foreach (var incrementalPlan in incrementalPlans)
        {
            foreach (var deliveryGroup in incrementalPlan.DeliveryGroups)
            {
                if (!deliveryPaths.ContainsKey(deliveryGroup.Id))
                {
                    deliveryPaths.Add(
                        deliveryGroup.Id,
                        CreateDeliveryPath(
                            incrementalPlan.Operation,
                            deliveryGroup.Path ?? SelectionPath.Root));
                }
            }
        }

        // A group that no plan delivers with is reached through the plan of a nested group.
        foreach (var incrementalPlan in incrementalPlans)
        {
            foreach (var deliveryGroup in incrementalPlan.DeliveryGroups)
            {
                for (var ancestor = deliveryGroup.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (!deliveryPaths.ContainsKey(ancestor.Id))
                    {
                        deliveryPaths.Add(
                            ancestor.Id,
                            CreateDeliveryPath(
                                incrementalPlan.Operation,
                                ancestor.Path ?? SelectionPath.Root));
                    }
                }
            }
        }

        foreach (var deliveryGroup in deliveryGroups)
        {
            if (!deliveryPaths.ContainsKey(deliveryGroup.Id))
            {
                deliveryPaths.Add(
                    deliveryGroup.Id,
                    CreateDeliveryPath(
                        operation,
                        deliveryGroup.Path ?? SelectionPath.Root));
            }
        }

        return deliveryPaths;
    }

    private static DeliveryPath CreateDeliveryPath(
        Operation operation,
        SelectionPath selectionPath)
    {
        var resolver = new DeliveryPathResolver(operation.Schema, selectionPath);

        if (resolver.TryResolve(
            0,
            operation.Definition.SelectionSet,
            operation.RootType,
            Path.Root,
            0,
            out var deliveryPath))
        {
            return deliveryPath;
        }

        throw ThrowHelper.DeliveryPathSegmentNotFound(resolver.UnresolvedSegmentName);
    }

    private static bool TryFindInlineFragment(
        SelectionSetNode selectionSet,
        string typeName,
        out InlineFragmentNode inlineFragment)
    {
        foreach (var selection in selectionSet.Selections)
        {
            if (selection is not InlineFragmentNode candidate)
            {
                continue;
            }

            if (candidate.TypeCondition?.Name.Value == typeName)
            {
                inlineFragment = candidate;
                return true;
            }

            if (TryFindInlineFragment(candidate.SelectionSet, typeName, out inlineFragment))
            {
                return true;
            }
        }

        inlineFragment = null!;
        return false;
    }

    private struct DeliveryPathResolver(ISchemaDefinition schema, SelectionPath selectionPath)
    {
        private int _unresolvedIndex;

        public readonly string UnresolvedSegmentName => selectionPath[_unresolvedIndex].Name;

        public bool TryResolve(
            int index,
            SelectionSetNode selectionSet,
            IOutputTypeDefinition typeContext,
            Path path,
            int pendingFieldCount,
            out DeliveryPath deliveryPath)
        {
            for (; index < selectionPath.Length; index++)
            {
                var segment = selectionPath[index];

                if (segment.Kind is SelectionPathSegmentKind.Root)
                {
                    continue;
                }

                if (segment.Kind is SelectionPathSegmentKind.InlineFragment)
                {
                    if (!TryFindInlineFragment(selectionSet, segment.Name, out var inlineFragment)
                        || !schema.Types.TryGetType<IOutputTypeDefinition>(segment.Name, out var fragmentType))
                    {
                        _unresolvedIndex = Math.Max(_unresolvedIndex, index);
                        deliveryPath = default;
                        return false;
                    }

                    selectionSet = inlineFragment.SelectionSet;
                    typeContext = fragmentType;
                    continue;
                }

                if (TryResolveField(index, selectionSet, typeContext, path, pendingFieldCount, out deliveryPath))
                {
                    return true;
                }

                _unresolvedIndex = Math.Max(_unresolvedIndex, index);
                return false;
            }

            deliveryPath = new DeliveryPath(path, pendingFieldCount);
            return true;
        }

        private bool TryResolveField(
            int index,
            SelectionSetNode selectionSet,
            IOutputTypeDefinition typeContext,
            Path path,
            int pendingFieldCount,
            out DeliveryPath deliveryPath)
        {
            var responseName = selectionPath[index].Name;

            foreach (var selection in selectionSet.Selections)
            {
                if (selection is FieldNode candidate
                    && (candidate.Alias?.Value ?? candidate.Name.Value) == responseName
                    && typeContext is IComplexTypeDefinition complexType
                    && complexType.Fields.TryGetField(candidate.Name.Value, out var field)
                    && field is not null)
                {
                    var fieldPath = path.Append(responseName);

                    if (field.Type.IsListType())
                    {
                        deliveryPath = new DeliveryPath(fieldPath, pendingFieldCount + 1);
                        return true;
                    }

                    var childSelectionSet = candidate.SelectionSet
                        ?? throw ThrowHelper.DeliveryPathSelectionSetMissing(responseName);

                    if (TryResolve(
                        index + 1,
                        childSelectionSet,
                        field.Type.NamedType<IOutputTypeDefinition>(),
                        fieldPath,
                        pendingFieldCount + 1,
                        out deliveryPath))
                    {
                        return true;
                    }
                }

                if (selection is InlineFragmentNode inlineFragment)
                {
                    IOutputTypeDefinition fragmentType;
                    if (inlineFragment.TypeCondition?.Name.Value is not { } typeName)
                    {
                        fragmentType = typeContext;
                    }
                    else if (schema.Types.TryGetType<IOutputTypeDefinition>(typeName, out var resolvedType)
                        && resolvedType is not null)
                    {
                        fragmentType = resolvedType;
                    }
                    else
                    {
                        continue;
                    }

                    if (TryResolveField(
                        index,
                        inlineFragment.SelectionSet,
                        fragmentType,
                        path,
                        pendingFieldCount,
                        out deliveryPath))
                    {
                        return true;
                    }
                }
            }

            deliveryPath = default;
            return false;
        }
    }

    internal readonly record struct DeliveryPath(Path PendingPath, int PendingFieldCount);
}
