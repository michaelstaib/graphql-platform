using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Utilities;
using static HotChocolate.Utilities.ErrorHelper;

namespace HotChocolate.Configuration.Validation;

internal sealed class ExecutionGroupValidationRule : ISchemaValidationRule
{
    public void Validate(
        IDescriptorContext context,
        ISchemaDefinition schema,
        ICollection<ISchemaError> errors)
    {
        foreach (var objectType in schema.Types.OfType<ObjectType>())
        {
            foreach (var field in objectType.Fields.AsSpan())
            {
                if (field.ExecutionGroup is { } group)
                {
                    ValidateGroup(schema, objectType, field, group, errors);
                }
            }
        }
    }

    private static void ValidateGroup(
        ISchemaDefinition schema,
        ObjectType type,
        ObjectField field,
        ExecutionGroup group,
        ICollection<ISchemaError> errors)
    {
        if (group.Entries.IsEmpty)
        {
            errors.Add(ExecutionGroup_NoEntries(type, field));
            return;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in group.Entries)
        {
            if (!paths.Add(entry.Path))
            {
                errors.Add(
                    ExecutionGroup_InvalidEntry(
                        type, 
                        field, 
                        entry, 
                        "is declared more than once."));
                continue;
            }

            if (TryValidateEntry(schema, field, entry, out var reason))
            {
                continue;
            }

            errors.Add(ExecutionGroup_InvalidEntry(type, field, entry, reason));
        }

        foreach (var entry in group.Entries)
        {
            foreach (var other in group.Entries)
            {
                if (IsNestedBelow(entry, other))
                {
                    errors.Add(
                        ExecutionGroup_InvalidEntry(
                            type,
                            field,
                            entry,
                            $"is nested below the entry `{other.Path}`."));
                    break;
                }
            }
        }
    }

    private static bool IsNestedBelow(ExecutionGroupEntry entry, ExecutionGroupEntry other)
    {
        if (other.Segments.Length >= entry.Segments.Length)
        {
            return false;
        }

        for (var i = 0; i < other.Segments.Length; i++)
        {
            if (!string.Equals(entry.Segments[i], other.Segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateEntry(
        ISchemaDefinition schema,
        ObjectField root,
        ExecutionGroupEntry entry,
        out string reason)
    {
        foreach (var name in entry.Segments)
        {
            if (!name.IsValidGraphQLName())
            {
                reason = $"contains the invalid field name `{name}`.";
                return false;
            }
        }

        switch (root.Type.NamedType())
        {
            case ObjectType objectType:
                return TryValidatePath(objectType, entry, out reason);

            case InterfaceType interfaceType:
                foreach (var possibleType in schema.GetPossibleTypes(interfaceType))
                {
                    if (!TryValidatePath(possibleType, entry, out reason))
                    {
                        return false;
                    }
                }

                reason = string.Empty;
                return true;

            default:
                reason =
                    "cannot be resolved because the type of the group root must be an object type "
                    + "or interface type.";
                return false;
        }
    }

    private static bool TryValidatePath(
        IComplexTypeDefinition currentType,
        ExecutionGroupEntry entry,
        out string reason)
    {
        for (var i = 0; i < entry.Segments.Length; i++)
        {
            var segment = entry.Segments[i];

            if (!currentType.Fields.TryGetField(segment, out var field))
            {
                reason = $"references the field `{segment}` that does not exist on type `{currentType.Name}`.";
                return false;
            }

            if (field is ObjectField { BatchResolver: not null })
            {
                reason = $"references the field `{currentType.Name}.{segment}` that uses a batch resolver.";
                return false;
            }

            if (field is ObjectField { IsParallelExecutable: false })
            {
                reason = $"references the field `{currentType.Name}.{segment}` that is not parallel executable.";
                return false;
            }

            if (i == entry.Segments.Length - 1)
            {
                break;
            }

            if (field.Type.IsListType())
            {
                reason = $"passes through the list field `{currentType.Name}.{segment}`.";
                return false;
            }

            if (field is ObjectField { ExecutionGroup: not null })
            {
                reason = $"passes through the field `{currentType.Name}.{segment}` that is an execution group root.";
                return false;
            }

            if (field.Type.NamedType() is not ObjectType nextType)
            {
                reason = $"passes through the field `{currentType.Name}.{segment}` that is not of an object type.";
                return false;
            }

            currentType = nextType;
        }

        reason = string.Empty;
        return true;
    }
}
