using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Rewrites an operation so that every <c>@defer</c> fragment that stays deferred has a plain copy
/// in front of it, and every <c>@defer</c> fragment that cannot be delivered later is inlined.
/// </summary>
internal static class DeferLowering
{
    /// <summary>
    /// Lowers the <c>@defer</c> fragments of <paramref name="operation"/>.
    /// </summary>
    /// <param name="operation">The operation with inlined fragment spreads.</param>
    /// <param name="schema">The composite schema that provides the lookup metadata.</param>
    /// <returns>
    /// An operation that selects the same fields as <paramref name="operation"/> for every variable value.
    /// </returns>
    public static OperationDefinitionNode Lower(OperationDefinitionNode operation, FusionSchemaDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(schema);

        var lowerer = new Lowerer(schema, operation.Operation);
        var rootItems = operation.SelectionSet.Selections;
        var rootScope = Scope.Root(schema.GetOperationType(operation.Operation), rootItems);
        var selections = lowerer.LowerList(rootItems, rootScope);

        return operation.WithSelectionSet(new SelectionSetNode(selections));
    }

    private sealed class Lowerer(FusionSchemaDefinition schema, OperationType operationType)
    {
        public List<ISelectionNode> LowerList(IReadOnlyList<ISelectionNode> items, Scope scope)
        {
            var result = new List<ISelectionNode>(items.Count);

            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: { } selectionSet } field:
                        var fieldScope = scope.EnterField(
                            GetFieldType(scope.Type, field),
                            selectionSet.Selections);
                        var lowered = LowerList(selectionSet.Selections, fieldScope);
                        result.Add(field.WithSelectionSet(new SelectionSetNode(lowered)));
                        break;

                    case InlineFragmentNode fragment:
                        LowerFragment(fragment, scope, result);
                        break;

                    default:
                        result.Add(item);
                        break;
                }
            }

            return result;
        }

        private void LowerFragment(InlineFragmentNode fragment, Scope scope, List<ISelectionNode> result)
        {
            var items = fragment.SelectionSet.Selections;
            var fragmentType = GetFragmentType(scope.Type, fragment);

            if (!HasDeferDirective(fragment))
            {
                var content = LowerList(items, scope.EnterFragment(fragmentType, fragment.TypeCondition, items, false));
                result.Add(fragment.WithSelectionSet(new SelectionSetNode(content)));
                return;
            }

            var isDeferrable = DeferCondition.TryCreate(fragment, out var condition);
            var plainScope = scope.EnterFragment(fragmentType, fragment.TypeCondition, items, false);
            List<ISelectionNode>? deferredContent = null;
            var coverage = Coverage.Empty;

            if (isDeferrable)
            {
                // The content is lowered first, so a nested fragment that was inlined is part of it.
                var deferredScope = scope.EnterFragment(fragmentType, fragment.TypeCondition, items, true);
                coverage = Coverage.Create(scope, fragment.TypeCondition);
                deferredContent = Prune(LowerList(items, deferredScope), coverage);
            }

            // The fragment is inlined when it cannot be delivered later or adds nothing.
            // The decision reads the content that is left after the pruning of the deferred copy.
            if (deferredContent is null || ShouldInline(deferredContent, fragmentType, coverage, scope))
            {
                var inlined = new InlineFragmentNode(
                    null,
                    fragment.TypeCondition,
                    fragment.Directives.Where(static d => !IsDeferDirective(d)).ToArray(),
                    new SelectionSetNode(LowerList(items, plainScope)));
                result.Add(inlined);
                return;
            }

            if (condition.IfVariableName is { } variableName)
            {
                result.Add(CreatePlainCopy(fragment, variableName, LowerList(items, plainScope)));
            }

            // The deferred copy drops the leaf fields that are selected outside of any deferred fragment.
            result.Add(fragment.WithSelectionSet(new SelectionSetNode(deferredContent)));
        }

        private bool ShouldInline(
            List<ISelectionNode> content,
            ITypeDefinition? anchorType,
            Coverage coverage,
            Scope scope)
        {
            // A mutation root field runs exactly once.
            if (scope.IsRoot && operationType is OperationType.Mutation)
            {
                return true;
            }

            // A fragment below the root is fetched through a direct lookup of its anchor type.
            if (!scope.IsRoot && !HasDirectLookups(content, anchorType))
            {
                return true;
            }

            // Nothing in the fragment is new.
            return !HasOwnField(content, coverage);
        }

        private static bool HasOwnField(IReadOnlyList<ISelectionNode> items, Coverage coverage)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: null }:
                        return true;

                    case FieldNode { SelectionSet: { } selectionSet } composite:
                        if (!coverage.Fields.TryGetValue(GetResponseName(composite), out var childCoverage)
                            || HasOwnField(selectionSet.Selections, childCoverage))
                        {
                            return true;
                        }

                        break;

                    // A nested fragment that stays deferred announces its own fields.
                    case InlineFragmentNode fragment when !HasDeferDirective(fragment)
                        && HasOwnField(fragment.SelectionSet.Selections, coverage):
                        return true;
                }
            }

            return false;
        }

        private bool HasDirectLookups(IReadOnlyList<ISelectionNode> items, ITypeDefinition? type)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode field when !HasDirectLookup(type, field):
                        return false;

                    case InlineFragmentNode fragment when !HasDeferDirective(fragment)
                        && !HasDirectLookups(fragment.SelectionSet.Selections, GetFragmentType(type, fragment)):
                        return false;
                }
            }

            return true;
        }

        private bool HasDirectLookup(ITypeDefinition? type, FieldNode field)
        {
            if (type is not FusionComplexTypeDefinition complexType)
            {
                return false;
            }

            // __typename is served by every source schema of the type.
            ImmutableArray<string> toSchemas;

            if (field.Name.Value.Equals(IntrospectionFieldNames.TypeName, StringComparison.Ordinal))
            {
                toSchemas = complexType.Sources.Schemas;
            }
            else if (complexType.Fields.TryGetField(field.Name.Value, out var fieldDefinition))
            {
                toSchemas = fieldDefinition.Sources.Schemas;
            }
            else
            {
                return false;
            }

            foreach (var toSchema in toSchemas)
            {
                foreach (var fromSchema in complexType.Sources.Schemas)
                {
                    if (schema.TryGetBestDirectLookup(complexType, fromSchema, toSchema, out _))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<ISelectionNode> Prune(IReadOnlyList<ISelectionNode> items, Coverage coverage)
        {
            var result = new List<ISelectionNode>(items.Count);

            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: null } leaf:
                        if (!coverage.Fields.ContainsKey(GetResponseName(leaf)))
                        {
                            result.Add(leaf);
                        }

                        break;

                    case FieldNode { SelectionSet: { } selectionSet } composite:
                        var childCoverage =
                            coverage.Fields.GetValueOrDefault(GetResponseName(composite)) ?? Coverage.Empty;
                        var children = Prune(selectionSet.Selections, childCoverage);

                        if (children.Count > 0)
                        {
                            result.Add(composite.WithSelectionSet(new SelectionSetNode(children)));
                        }

                        break;

                    case InlineFragmentNode fragment when !HasDeferDirective(fragment):
                        var content = Prune(fragment.SelectionSet.Selections, coverage);

                        if (content.Count > 0)
                        {
                            result.Add(fragment.WithSelectionSet(new SelectionSetNode(content)));
                        }

                        break;

                    default:
                        result.Add(item);
                        break;
                }
            }

            return result;
        }

        private static InlineFragmentNode CreatePlainCopy(
            InlineFragmentNode fragment,
            string variableName,
            List<ISelectionNode> content)
        {
            var skip = new DirectiveNode(
                DirectiveNames.Skip.Name,
                new ArgumentNode(DirectiveNames.Skip.Arguments.If, new VariableNode(variableName)));
            var directives = fragment.Directives.Where(static d => !IsDeferDirective(d)).ToList();
            var selectionSet = new SelectionSetNode(content);

            if (!directives.Exists(static d => d.Name.Value.Equals(DirectiveNames.Skip.Name, StringComparison.Ordinal)))
            {
                directives.Add(skip);
                return new InlineFragmentNode(null, fragment.TypeCondition, directives, selectionSet);
            }

            // A fragment has one @skip, so the second condition goes onto a nested fragment.
            var inner = new InlineFragmentNode(null, null, [skip], selectionSet);
            return new InlineFragmentNode(
                null,
                fragment.TypeCondition,
                directives,
                new SelectionSetNode([inner]));
        }

        private static ITypeDefinition? GetFieldType(ITypeDefinition? type, FieldNode field)
            => type is IComplexTypeDefinition complexType
                && complexType.Fields.TryGetField(field.Name.Value, out var fieldDefinition)
                    ? fieldDefinition.Type.NamedType()
                    : null;

        private ITypeDefinition? GetFragmentType(ITypeDefinition? type, InlineFragmentNode fragment)
        {
            if (fragment.TypeCondition is null)
            {
                return type;
            }

            return schema.Types.TryGetType(fragment.TypeCondition.Name.Value, allowInaccessibleFields: true, out var t)
                ? t
                : null;
        }

        private static string GetResponseName(FieldNode field)
            => field.Alias?.Value ?? field.Name.Value;

        private static bool HasDeferDirective(InlineFragmentNode fragment)
        {
            foreach (var directive in fragment.Directives)
            {
                if (IsDeferDirective(directive))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDeferDirective(DirectiveNode directive)
            => directive.Name.Value.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The position of a selection list: the type of its fields, the lists that enclose it on the same
    /// field level, and whether it sits in a deferred fragment.
    /// </summary>
    private sealed class Scope
    {
        private Scope(ITypeDefinition? type, bool isRoot, bool inDeferred, Level level)
        {
            Type = type;
            IsRoot = isRoot;
            InDeferred = inDeferred;
            Level = level;
        }

        public ITypeDefinition? Type { get; }

        public bool IsRoot { get; }

        public bool InDeferred { get; }

        public Level Level { get; }

        public static Scope Root(ITypeDefinition type, IReadOnlyList<ISelectionNode> items)
            => new(type, true, false, new Level(items, true, null, null));

        public Scope EnterField(ITypeDefinition? type, IReadOnlyList<ISelectionNode> items)
            => new(type, false, InDeferred, new Level(items, !InDeferred, null, null));

        public Scope EnterFragment(
            ITypeDefinition? type,
            NamedTypeNode? typeCondition,
            IReadOnlyList<ISelectionNode> items,
            bool deferred)
        {
            var inDeferred = InDeferred || deferred;
            return new Scope(type, IsRoot, inDeferred, new Level(items, !inDeferred, typeCondition, Level));
        }
    }

    /// <summary>
    /// A selection list on one field level. <paramref name="IsOutsideDeferred"/> is <c>true</c> when no
    /// deferred fragment encloses the list.
    /// </summary>
    private sealed record Level(
        IReadOnlyList<ISelectionNode> Items,
        bool IsOutsideDeferred,
        NamedTypeNode? OwnerTypeCondition,
        Level? Outer);

    /// <summary>
    /// The fields that are selected without conditions outside of any deferred fragment at a place.
    /// </summary>
    private sealed class Coverage
    {
        public static readonly Coverage Empty = new();

        public Dictionary<string, Coverage> Fields { get; } = new(StringComparer.Ordinal);

        public static Coverage Create(Scope scope, NamedTypeNode? typeCondition)
        {
            var coverage = new Coverage();
            var typeConditions = new HashSet<string>(StringComparer.Ordinal);

            if (typeCondition is not null)
            {
                typeConditions.Add(typeCondition.Name.Value);
            }

            for (var level = scope.Level; level is not null; level = level.Outer)
            {
                if (level.IsOutsideDeferred)
                {
                    coverage.Collect(level.Items, typeConditions);
                }

                if (level.OwnerTypeCondition is not null)
                {
                    typeConditions.Add(level.OwnerTypeCondition.Name.Value);
                }
            }

            return coverage;
        }

        private void Collect(IReadOnlyList<ISelectionNode> items, HashSet<string> typeConditions)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { Directives.Count: 0 } field:
                        var name = field.Alias?.Value ?? field.Name.Value;

                        if (!Fields.TryGetValue(name, out var child))
                        {
                            child = new Coverage();
                            Fields.Add(name, child);
                        }

                        if (field.SelectionSet is { } selectionSet)
                        {
                            child.Collect(selectionSet.Selections, []);
                        }

                        break;

                    case InlineFragmentNode { Directives.Count: 0 } fragment
                        when fragment.TypeCondition is null
                            || typeConditions.Contains(fragment.TypeCondition.Name.Value):
                        Collect(fragment.SelectionSet.Selections, typeConditions);
                        break;
                }
            }
        }
    }
}
