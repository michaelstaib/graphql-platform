using HotChocolate.Fusion.Directives;
using HotChocolate.Fusion.Events;
using HotChocolate.Fusion.Events.Contracts;
using HotChocolate.Fusion.Extensions;
using HotChocolate.Fusion.Language;
using HotChocolate.Fusion.SchemaVisitors;
using HotChocolate.Fusion.Validators;
using HotChocolate.Types;
using HotChocolate.Types.Mutable;
using static HotChocolate.Fusion.Logging.LogEntryHelper;
using static HotChocolate.Fusion.WellKnownArgumentNames;
using static HotChocolate.Fusion.WellKnownDirectiveNames;

namespace HotChocolate.Fusion.PostMergeValidationRules;

/// <summary>
/// Fails composition with <c>AUTHORIZATION_TRANSITIVE_REQUIREMENTS_MISSING</c> when a field reads
/// other fields through <c>@require</c> or a lookup key without declaring all of their
/// authorization requirements. A requirement is declared when every alternative group of it
/// contains a group of the dependency and <c>@authenticated</c> is present whenever the dependency
/// has it.
/// </summary>
internal sealed class AuthorizationTransitiveRequirementsRule : IEventHandler<SchemaEvent>
{
    public void Handle(SchemaEvent @event, CompositionContext context)
    {
        var schema = @event.Schema;

        if (!HasAuthorization(schema))
        {
            return;
        }

        var checker = new Checker(schema, context);

        foreach (var sourceSchema in context.SchemaDefinitions)
        {
            checker.CheckRequireDirectives(sourceSchema);
            checker.CheckLookupKeys(sourceSchema);
        }
    }

    private static bool HasAuthorization(MutableSchemaDefinition schema)
    {
        foreach (var type in schema.Types.OfType<MutableComplexTypeDefinition>())
        {
            foreach (var field in type.Fields)
            {
                if (field.Directives.ContainsName(FusionAuthorization))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class Checker(MutableSchemaDefinition schema, CompositionContext context)
    {
        private readonly FieldSelectionMapValidator _validator = new(schema);
        private readonly HashSet<(string Requiring, string Dependency)> _reported = [];

        public void CheckRequireDirectives(MutableSchemaDefinition sourceSchema)
        {
            foreach (var sourceType in sourceSchema.Types.OfType<MutableComplexTypeDefinition>())
            {
                if (!schema.Types.TryGetType<MutableComplexTypeDefinition>(
                        sourceType.Name,
                        out var mergedType))
                {
                    continue;
                }

                foreach (var sourceField in sourceType.Fields)
                {
                    if (!mergedType.Fields.TryGetField(sourceField.Name, out var mergedField))
                    {
                        continue;
                    }

                    foreach (var argument in sourceField.Arguments)
                    {
                        if (!argument.HasRequireDirective
                            || argument.Directives[Require].First().Arguments[Field].Value
                                is not string map)
                        {
                            continue;
                        }

                        var dependencies = SelectFields(map, argument, mergedType);

                        Check(
                            mergedField,
                            dependencies,
                            new SchemaCoordinate(sourceType.Name, sourceField.Name, argument.Name),
                            sourceSchema);
                    }
                }
            }
        }

        public void CheckLookupKeys(MutableSchemaDefinition sourceSchema)
        {
            var lookups = new DiscoverLookupsSchemaVisitor(sourceSchema).Discover();

            foreach (var (typeName, lookupGroup) in lookups)
            {
                if (!schema.Types.TryGetType<MutableComplexTypeDefinition>(typeName, out var mergedType))
                {
                    continue;
                }

                foreach (var (lookupField, _, _) in lookupGroup)
                {
                    CheckLookupKey(lookupField, mergedType, sourceSchema);
                }
            }
        }

        private void CheckLookupKey(
            MutableOutputFieldDefinition lookupField,
            MutableComplexTypeDefinition mergedType,
            MutableSchemaDefinition sourceSchema)
        {
            if (lookupField.Type.AsTypeDefinition() is not MutableComplexTypeDefinition sourceType)
            {
                return;
            }

            var dependencies = new List<IOutputFieldDefinition>();

            foreach (var argument in lookupField.Arguments)
            {
                if (!argument.HasRequireDirective)
                {
                    dependencies.AddRange(
                        SelectFields(
                            argument.GetIsFieldSelectionMap() ?? argument.Name,
                            argument,
                            mergedType));
                }
            }

            dependencies.RemoveAll(d => !IsServedByOtherSchema(d, sourceSchema));

            var keyFieldNames = dependencies
                .Where(d => d.Coordinate.Name == mergedType.Name)
                .Select(d => d.Coordinate.MemberName)
                .ToHashSet();

            var servedTypes = new List<MutableComplexTypeDefinition> { sourceType };

            if (sourceType is MutableInterfaceTypeDefinition)
            {
                servedTypes.AddRange(sourceSchema.GetPossibleTypes(sourceType));
            }

            foreach (var servedType in servedTypes)
            {
                if (!schema.Types.TryGetType<MutableComplexTypeDefinition>(
                        servedType.Name,
                        out var mergedServedType))
                {
                    continue;
                }

                foreach (var servedField in servedType.Fields)
                {
                    if (servedField is { IsExternal: false, IsInternal: false, IsOverridden: false }
                        && !keyFieldNames.Contains(servedField.Name)
                        && mergedServedType.Fields.TryGetField(servedField.Name, out var mergedField))
                    {
                        Check(mergedField, dependencies, lookupField.Coordinate, sourceSchema);
                    }
                }
            }
        }

        private bool IsServedByOtherSchema(
            IOutputFieldDefinition field,
            MutableSchemaDefinition sourceSchema)
        {
            foreach (var other in context.SchemaDefinitions)
            {
                if (!ReferenceEquals(other, sourceSchema)
                    && other.Types.TryGetType<MutableComplexTypeDefinition>(
                        field.Coordinate.Name,
                        out var otherType)
                    && otherType.Fields.TryGetField(field.Name, out var otherField)
                    && otherField is { IsExternal: false, IsInternal: false, IsOverridden: false })
                {
                    return true;
                }
            }

            return false;
        }

        private List<IOutputFieldDefinition> SelectFields(
            string map,
            MutableInputFieldDefinition argument,
            MutableComplexTypeDefinition mergedType)
        {
            var inputTypeName = argument.Type.AsTypeDefinition().Name;

            if (!schema.Types.TryGetType(inputTypeName, out var inputTypeDefinition))
            {
                return [];
            }

            var inputType = argument.Type.ToTypeNode().RewriteToType(inputTypeDefinition);

            _validator.Validate(
                new FieldSelectionMapParser(map).Parse(),
                inputType,
                mergedType,
                out var selectedFields);

            return [.. selectedFields.OrderBy(f => f.Coordinate.ToString(), StringComparer.Ordinal)];
        }

        private void Check(
            IOutputFieldDefinition requiring,
            List<IOutputFieldDefinition> dependencies,
            SchemaCoordinate via,
            MutableSchemaDefinition sourceSchema)
        {
            var requirement = GetRequirement(requiring);

            foreach (var dependency in dependencies)
            {
                var uncovered = requirement.GetUncovered(GetRequirement(dependency));

                if (uncovered.IsEmpty
                    || !_reported.Add((requiring.Coordinate.ToString(), dependency.Coordinate.ToString())))
                {
                    continue;
                }

                context.Log.Write(
                    AuthorizationTransitiveRequirementsMissing(
                        requiring.Coordinate,
                        dependency.Coordinate,
                        via,
                        sourceSchema,
                        uncovered));
            }
        }

        private static MergedAuthorization GetRequirement(IOutputFieldDefinition field)
        {
            return field.Directives.FirstOrDefault(FusionAuthorization) is { } directive
                ? AuthorizationGroups.FromFusionDirective(directive)
                : MergedAuthorization.Empty;
        }
    }
}
