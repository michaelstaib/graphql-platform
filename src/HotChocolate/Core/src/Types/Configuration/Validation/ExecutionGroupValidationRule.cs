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
                if (field.ExecutionGroup is { } definition)
                {
                    ValidateGroup(objectType, field, definition, errors);
                }
            }
        }
    }

    private static void ValidateGroup(
        ObjectType type,
        ObjectField field,
        ExecutionGroupDefinition definition,
        ICollection<ISchemaError> errors)
    {
        if (definition.Entries.IsEmpty)
        {
            errors.Add(ExecutionGroup_NoEntries(type, field));
            return;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in definition.Entries)
        {
            if (!paths.Add(entry.Path))
            {
                errors.Add(ExecutionGroup_InvalidEntry(type, field, entry, "is declared more than once."));
                continue;
            }

            if (TryValidateEntry(field, entry, out var reason))
            {
                continue;
            }

            errors.Add(ExecutionGroup_InvalidEntry(type, field, entry, reason));
        }
    }

    private static bool TryValidateEntry(ObjectField root, ExecutionGroupEntry entry, out string reason)
    {
        foreach (var name in entry.Segments)
        {
            if (!name.IsValidGraphQLName())
            {
                reason = $"contains the invalid field name `{name}`.";
                return false;
            }
        }

        if (root.Type.NamedType() is not IComplexTypeDefinition currentType)
        {
            reason = "cannot be resolved because the type of the group root must be an object type or interface type.";
            return false;
        }

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
