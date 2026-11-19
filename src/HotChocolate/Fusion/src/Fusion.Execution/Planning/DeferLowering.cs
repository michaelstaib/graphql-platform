using System.Collections.Immutable;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Rewrites an operation so that every switchable <c>@defer</c> fragment becomes a plain copy and a deferred
/// copy of which exactly one is selected per request, and every <c>@defer</c> fragment that cannot be delivered
/// later is inlined.
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
        var selections = lowerer.LowerPass(rootItems, rootScope);

        return operation.WithSelectionSet(new SelectionSetNode(lowerer.Finish(selections)));
    }

    /// <summary>
    /// Numbers the delivery groups of a lowered and normalized operation densely in the order of their first
    /// <c>@defer</c> fragments, a parent before its children.
    /// </summary>
    /// <param name="operation">The lowered and normalized operation.</param>
    /// <returns>
    /// The operation with the new numbers, or <paramref name="operation"/> when its numbers are already in order or
    /// a parent number names no group.
    /// </returns>
    public static OperationDefinitionNode Renumber(OperationDefinitionNode operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var order = new List<int>();
        var parents = new Dictionary<int, int>();
        CollectGroups(operation.SelectionSet.Selections, order, parents);

        foreach (var parent in parents.Values)
        {
            if (parent >= 0 && !parents.ContainsKey(parent))
            {
                return operation;
            }
        }

        var numbers = new Dictionary<int, int>(order.Count);

        foreach (var group in order)
        {
            AssignNumber(group, parents, numbers);
        }

        var unchanged = true;

        foreach (var (before, after) in numbers)
        {
            unchanged &= before == after;
        }

        if (unchanged)
        {
            return operation;
        }

        var selections = RenumberList(operation.SelectionSet.Selections, numbers);
        return operation.WithSelectionSet(new SelectionSetNode(selections));
    }

    private static void AssignNumber(int group, Dictionary<int, int> parents, Dictionary<int, int> numbers)
    {
        if (numbers.ContainsKey(group))
        {
            return;
        }

        if (parents.TryGetValue(group, out var parent) && parent >= 0 && parents.ContainsKey(parent))
        {
            AssignNumber(parent, parents, numbers);
        }

        numbers.Add(group, numbers.Count);
    }

    private static void CollectGroups(
        IReadOnlyList<ISelectionNode> items,
        List<int> order,
        Dictionary<int, int> parents)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case FieldNode { SelectionSet: { } selectionSet }:
                    CollectGroups(selectionSet.Selections, order, parents);
                    break;

                case InlineFragmentNode fragment:
                    foreach (var directive in fragment.Directives)
                    {
                        if (IsDeferDirective(directive)
                            && TryGetInt(directive, DocumentRewriter.DeferArguments.Group, out var group)
                            && TryGetInt(directive, DocumentRewriter.DeferArguments.Parent, out var parent)
                            && parents.TryAdd(group, parent))
                        {
                            order.Add(group);
                        }
                    }

                    CollectGroups(fragment.SelectionSet.Selections, order, parents);
                    break;
            }
        }
    }

    private static bool TryGetInt(DirectiveNode directive, string name, out int value)
    {
        foreach (var argument in directive.Arguments)
        {
            if (argument.Name.Value.Equals(name, StringComparison.Ordinal) && argument.Value is IntValueNode number)
            {
                value = number.ToInt32();
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static List<ISelectionNode> RenumberList(
        IReadOnlyList<ISelectionNode> items,
        Dictionary<int, int> numbers)
    {
        var result = new List<ISelectionNode>(items.Count);

        foreach (var item in items)
        {
            switch (item)
            {
                case FieldNode { SelectionSet: { } selectionSet } field:
                    var children = RenumberList(selectionSet.Selections, numbers);
                    result.Add(field.WithSelectionSet(new SelectionSetNode(children)));
                    break;

                case InlineFragmentNode fragment:
                    var directives = new List<DirectiveNode>(fragment.Directives.Count);

                    foreach (var directive in fragment.Directives)
                    {
                        directives.Add(IsDeferDirective(directive) ? RenumberDirective(directive, numbers) : directive);
                    }

                    var content = RenumberList(fragment.SelectionSet.Selections, numbers);
                    result.Add(fragment.WithDirectives(directives).WithSelectionSet(new SelectionSetNode(content)));
                    break;

                default:
                    result.Add(item);
                    break;
            }
        }

        return result;
    }

    private static DirectiveNode RenumberDirective(DirectiveNode directive, Dictionary<int, int> numbers)
    {
        var arguments = new List<ArgumentNode>(directive.Arguments.Count);

        foreach (var argument in directive.Arguments)
        {
            if (argument.Name.Value is DocumentRewriter.DeferArguments.Group or DocumentRewriter.DeferArguments.Parent
                && argument.Value is IntValueNode number
                && numbers.TryGetValue(number.ToInt32(), out var renumbered))
            {
                arguments.Add(new ArgumentNode(argument.Name.Value, new IntValueNode(renumbered)));
            }
            else
            {
                arguments.Add(argument);
            }
        }

        return new DirectiveNode(directive.Name.Value, arguments.ToArray());
    }

    private static bool IsDeferDirective(DirectiveNode directive)
        => directive.Name.Value.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal);

    private sealed class Lowerer(FusionSchemaDefinition schema, OperationType operationType)
    {
        private readonly Dictionary<InlineFragmentNode, Site> _sites = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<InlineFragmentNode> _inlined = new(ReferenceEqualityComparer.Instance);

        public List<ISelectionNode> LowerPass(IReadOnlyList<ISelectionNode> items, Scope scope)
        {
            var lowered = LowerList(items, scope);
            return AttachParts(lowered, scope.Pass, scope.Sink);
        }

        private List<ISelectionNode> LowerList(IReadOnlyList<ISelectionNode> items, Scope scope)
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
                        var output = field.WithSelectionSet(new SelectionSetNode(lowered));
                        scope.Pass.Outputs[field] = output;
                        result.Add(output);
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
                var content = LowerList(items, scope.EnterFragment(fragmentType, fragment, items));
                result.Add(fragment.WithSelectionSet(new SelectionSetNode(content)));
                return;
            }

            if (!_inlined.Contains(fragment)
                && DeferCondition.TryCreate(fragment, out var condition)
                && TryLowerDeferred(fragment, condition, fragmentType, scope, result))
            {
                return;
            }

            // The fragment is inlined when it cannot be delivered later or adds nothing.
            var inlined = new InlineFragmentNode(
                null,
                fragment.TypeCondition,
                fragment.Directives.Where(static d => !IsDeferDirective(d)).ToArray(),
                new SelectionSetNode(LowerList(items, scope.EnterFragment(fragmentType, fragment, items))));
            result.Add(inlined);
        }

        private bool TryLowerDeferred(
            InlineFragmentNode fragment,
            DeferCondition condition,
            ITypeDefinition? fragmentType,
            Scope scope,
            List<ISelectionNode> result)
        {
            var items = fragment.SelectionSet.Selections;
            var group = new DeferGroup(scope.Group);
            var (attempt, content, coverage, state) = AttemptDeferred(
                fragment,
                condition,
                fragmentType,
                scope,
                group);

            // The collected nested fragments are inlined, and the attempt repeats until it collects none that is
            // not inlined yet.
            while (!_inlined.IsSupersetOf(state.NestedToInline))
            {
                _inlined.UnionWith(state.NestedToInline);
                (attempt, content, coverage, state) = AttemptDeferred(
                    fragment,
                    condition,
                    fragmentType,
                    scope,
                    group);
            }

            // The decision reads the content that is left after the pruning of the deferred copy.
            if (ShouldInline(content, fragmentType, coverage, scope, state))
            {
                return false;
            }

            if (condition.IfVariableName is { } variableName)
            {
                var copyScope = scope.EnterCopy(fragmentType, fragment, items);
                result.Add(CreatePlainCopy(fragment, variableName, LowerPass(items, copyScope)));
                scope.Sink.AddRange(copyScope.Sink);
            }

            if (content.Count > 0)
            {
                // The deferred copy drops the leaf fields that an enclosing selection at the same place selects.
                var (copy, deferred) = CreateDeferredCopy(fragment, condition, content);
                _sites[deferred] = new Site(group, 0) { Origin = fragment };
                result.Add(copy);
            }
            else
            {
                // The group stands at its place without a fragment, so the numbering sees where it was.
                var marker = new InlineFragmentNode(null, null, [], new SelectionSetNode([]));
                _sites[marker] = new Site(group, 0) { IsMarker = true };
                result.Add(marker);
            }

            scope.Sink.AddRange(attempt.Sink);
            scope.Sink.AddRange(state.Parts);
            return true;
        }

        private DeferAttempt AttemptDeferred(
            InlineFragmentNode fragment,
            DeferCondition condition,
            ITypeDefinition? fragmentType,
            Scope scope,
            DeferGroup group)
        {
            var items = fragment.SelectionSet.Selections;

            // The content is lowered first, so a nested fragment that was inlined is part of it.
            var attempt = scope.EnterDeferred(fragmentType, fragment, items, group);
            var lowered = LowerPass(items, attempt);
            var coverage = Coverage.Create(scope, fragment.TypeCondition);
            var state = new PruneState(fragment, group, condition.IfVariableName);
            var content = Prune(lowered, coverage, fragmentType, 0, false, state);
            return new DeferAttempt(attempt, content, coverage, state);
        }

        private bool ShouldInline(
            List<ISelectionNode> content,
            ITypeDefinition? anchorType,
            Coverage coverage,
            Scope scope,
            PruneState state)
        {
            // A part that cannot keep its place in the outside selection of its list.
            if (state.MustInline)
            {
                return true;
            }

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

            // A part is fetched through a direct lookup of the item type of its list.
            foreach (var part in state.Parts)
            {
                if (!HasDirectLookups(part.Content, part.ItemType))
                {
                    return true;
                }
            }

            // Nothing in the fragment is new.
            return !HasOwnField(content, coverage)
                && !state.Parts.Exists(static part => HasOwnField(part.Content, Coverage.Empty));
        }

        private static bool HasOwnField(IReadOnlyList<ISelectionNode> items, Coverage coverage)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    // An introspection field is not an own field of a deferred fragment, nor is anything below it.
                    case FieldNode field when field.Name.Value.StartsWith("__", StringComparison.Ordinal):
                        break;

                    case FieldNode { SelectionSet: null }:
                        return true;

                    case FieldNode { SelectionSet: { } selectionSet } composite:
                        if (!coverage.Fields.TryGetValue(GetResponseName(composite), out var covered)
                            || HasOwnField(selectionSet.Selections, covered.Child))
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

        private List<ISelectionNode> Prune(
            IReadOnlyList<ISelectionNode> items,
            Coverage coverage,
            ITypeDefinition? type,
            int depth,
            bool typed,
            PruneState state)
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
                        PruneComposite(composite, selectionSet, coverage, type, depth, typed, state, result);
                        break;

                    case InlineFragmentNode marker when IsMarker(marker):
                        result.Add(marker);
                        break;

                    // Collects the nested fragments below a covered field that select a list of the outside selection.
                    case InlineFragmentNode nested when depth > 0 && HasDeferDirective(nested):
                        CollectNestedToInline(nested, coverage, type, state.NestedToInline);
                        result.Add(nested);
                        break;

                    case InlineFragmentNode fragment when !HasDeferDirective(fragment):
                        var mark = state.Push(fragment.Directives);
                        var content = Prune(
                            fragment.SelectionSet.Selections,
                            coverage,
                            GetFragmentType(type, fragment),
                            depth,
                            typed || fragment.TypeCondition is not null,
                            state);
                        state.Pop(mark);

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

        private void PruneComposite(
            FieldNode composite,
            SelectionSetNode selectionSet,
            Coverage coverage,
            ITypeDefinition? type,
            int depth,
            bool typed,
            PruneState state,
            List<ISelectionNode> result)
        {
            var responseName = GetResponseName(composite);
            var isList = IsListField(type, composite);

            if (!coverage.Fields.TryGetValue(responseName, out var covered))
            {
                // The outside selection of a list that D selects carries a condition or a type condition.
                if (coverage.ConditionalFields.TryGetValue(responseName, out var conditional))
                {
                    var conditionalType = GetFieldType(type, composite);

                    if (isList || SelectsConditionalList(selectionSet.Selections, conditional, conditionalType))
                    {
                        state.MustInline = true;
                    }

                    // A nested deferred fragment that selects a list of the outside selection is inlined, whether
                    // or not the enclosing fragment selects that list itself.
                    CollectNestedToInline(selectionSet.Selections, conditional, conditionalType, state.NestedToInline);
                }

                result.Add(composite);
                return;
            }

            var fieldType = GetFieldType(type, composite);
            var mark = state.Push(composite.Directives);
            var children = Prune(selectionSet.Selections, covered.Child, fieldType, depth + 1, typed, state);

            if (!isList)
            {
                state.Pop(mark);

                if (children.Count > 0)
                {
                    result.Add(composite.WithSelectionSet(new SelectionSetNode(children)));
                }

                return;
            }

            // Below a covered list the deferred copy keeps nothing. What is left becomes a part of the
            // outside selection of the list.
            if (children.Count > 0)
            {
                CreatePart(covered, children, fieldType, depth + 1, typed, state);
            }

            state.Pop(mark);
        }

        private bool CollectNestedToInline(
            IReadOnlyList<ISelectionNode> items,
            Coverage coverage,
            ITypeDefinition? type,
            List<InlineFragmentNode> toInline)
        {
            var found = false;

            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: { } selectionSet } field:
                        var responseName = GetResponseName(field);
                        var isList = IsListField(type, field);
                        var fieldType = GetFieldType(type, field);

                        if (coverage.Fields.TryGetValue(responseName, out var covered))
                        {
                            var below = CollectNestedToInline(
                                selectionSet.Selections,
                                covered.Child,
                                fieldType,
                                toInline);
                            found |= isList || below;
                        }
                        else if (coverage.ConditionalFields.TryGetValue(responseName, out var conditional))
                        {
                            var belowConditional = CollectNestedToInline(
                                selectionSet.Selections,
                                conditional,
                                fieldType,
                                toInline);
                            found |= isList
                                || belowConditional
                                || SelectsConditionalList(selectionSet.Selections, conditional, fieldType);
                        }

                        break;

                    case InlineFragmentNode fragment:
                        found |= CollectNestedToInline(fragment, coverage, type, toInline);
                        break;
                }
            }

            return found;
        }

        private bool CollectNestedToInline(
            InlineFragmentNode fragment,
            Coverage coverage,
            ITypeDefinition? type,
            List<InlineFragmentNode> toInline)
        {
            var selects = CollectNestedToInline(
                fragment.SelectionSet.Selections,
                coverage,
                GetFragmentType(type, fragment),
                toInline);

            if (selects
                && _sites.TryGetValue(fragment, out var site)
                && site is { IsMarker: false, Origin: { } origin })
            {
                toInline.Add(origin);
            }

            return selects;
        }

        private bool SelectsConditionalList(
            IReadOnlyList<ISelectionNode> items,
            Coverage conditional,
            ITypeDefinition? type)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: { } selectionSet } field
                        when conditional.ConditionalFields.TryGetValue(GetResponseName(field), out var below)
                            && (IsListField(type, field)
                                || SelectsConditionalList(selectionSet.Selections, below, GetFieldType(type, field))):
                        return true;

                    case InlineFragmentNode fragment when !HasDeferDirective(fragment)
                        && SelectsConditionalList(
                            fragment.SelectionSet.Selections,
                            conditional,
                            GetFragmentType(type, fragment)):
                        return true;
                }
            }

            return false;
        }

        private void CreatePart(
            CoveredField covered,
            List<ISelectionNode> content,
            ITypeDefinition? itemType,
            int levels,
            bool typed,
            PruneState state)
        {
            var fragment = state.Fragment;

            if (typed
                || state.Unsupported > 0
                || !covered.Attachable
                || fragment.TypeCondition is not null)
            {
                state.MustInline = true;
                return;
            }

            var deferDirective = fragment.Directives.First(static d => IsDeferDirective(d));
            var conditions = fragment.Directives.Where(static d => !IsDeferDirective(d)).ToList();
            var coreDirectives = new List<DirectiveNode>(2);

            if (state.SwitchVariable is { } variableName
                && !conditions.Exists(
                    d => d.Name.Value.Equals(DirectiveNames.Include.Name, StringComparison.Ordinal)
                        && IsConditionDirective(d, variableName)))
            {
                coreDirectives.Add(CreateConditionDirective(DirectiveNames.Include.Name, variableName));
            }

            coreDirectives.Add(deferDirective);
            conditions.AddRange(state.Conditions);

            var core = new InlineFragmentNode(null, null, coreDirectives, new SelectionSetNode(content));
            _sites[core] = new Site(state.Group, levels) { Origin = fragment };
            var outermost = core;

            for (var i = conditions.Count - 1; i >= 0; i--)
            {
                outermost = new InlineFragmentNode(
                    null,
                    null,
                    [conditions[i]],
                    new SelectionSetNode([outermost]));
            }

            state.Parts.Add(new PendingPart(covered.Pass, covered.Node, outermost, itemType, content));
        }

        private static List<ISelectionNode> AttachParts(
            List<ISelectionNode> items,
            Pass pass,
            List<PendingPart> sink)
        {
            Dictionary<FieldNode, List<InlineFragmentNode>>? byOutput = null;

            foreach (var part in sink)
            {
                if (part.Target != pass)
                {
                    continue;
                }

                byOutput ??= new Dictionary<FieldNode, List<InlineFragmentNode>>(ReferenceEqualityComparer.Instance);
                var output = pass.Outputs[part.Input];

                if (!byOutput.TryGetValue(output, out var parts))
                {
                    parts = [];
                    byOutput.Add(output, parts);
                }

                parts.Add(part.Fragment);
            }

            if (byOutput is null)
            {
                return items;
            }

            sink.RemoveAll(part => part.Target == pass);
            return Attach(items, byOutput) ?? items;
        }

        private static List<ISelectionNode>? Attach(
            IReadOnlyList<ISelectionNode> items,
            Dictionary<FieldNode, List<InlineFragmentNode>> byOutput)
        {
            List<ISelectionNode>? result = null;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                ISelectionNode? replacement = null;

                switch (item)
                {
                    case FieldNode { SelectionSet: { } selectionSet } field:
                        var children = Attach(selectionSet.Selections, byOutput);

                        if (byOutput.TryGetValue(field, out var parts))
                        {
                            children = [.. children ?? selectionSet.Selections, .. parts];
                        }

                        if (children is not null)
                        {
                            replacement = field.WithSelectionSet(new SelectionSetNode(children));
                        }

                        break;

                    case InlineFragmentNode fragment when !HasDeferDirective(fragment):
                        var content = Attach(fragment.SelectionSet.Selections, byOutput);

                        if (content is not null)
                        {
                            replacement = fragment.WithSelectionSet(new SelectionSetNode(content));
                        }

                        break;
                }

                if (replacement is null)
                {
                    result?.Add(item);
                    continue;
                }

                result ??= [.. items.Take(i)];
                result.Add(replacement);
            }

            return result;
        }

        public List<ISelectionNode> Finish(List<ISelectionNode> items)
        {
            if (_sites.Count == 0)
            {
                return items;
            }

            var next = 0;
            Number(items, ref next);
            return Write(items);
        }

        private void Number(IReadOnlyList<ISelectionNode> items, ref int next)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: { } selectionSet }:
                        Number(selectionSet.Selections, ref next);
                        break;

                    case InlineFragmentNode fragment:
                        if (_sites.TryGetValue(fragment, out var site) && site.Levels == 0)
                        {
                            AssignNumber(site.Group, ref next);
                        }

                        Number(fragment.SelectionSet.Selections, ref next);
                        break;
                }
            }
        }

        private static void AssignNumber(DeferGroup group, ref int next)
        {
            if (group.Number >= 0)
            {
                return;
            }

            if (group.Parent is { } parent)
            {
                AssignNumber(parent, ref next);
            }

            group.Number = next++;
        }

        private List<ISelectionNode> Write(IReadOnlyList<ISelectionNode> items)
        {
            var result = new List<ISelectionNode>(items.Count);

            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { SelectionSet: { } selectionSet } field:
                        var children = Write(selectionSet.Selections);

                        if (children.Count > 0 || selectionSet.Selections.Count == 0)
                        {
                            result.Add(field.WithSelectionSet(new SelectionSetNode(children)));
                        }

                        break;

                    case InlineFragmentNode fragment:
                        _sites.TryGetValue(fragment, out var site);

                        if (site is { IsMarker: true })
                        {
                            break;
                        }

                        var content = Write(fragment.SelectionSet.Selections);

                        if (content.Count == 0 && fragment.SelectionSet.Selections.Count > 0)
                        {
                            break;
                        }

                        var written = fragment.WithSelectionSet(new SelectionSetNode(content));
                        result.Add(site is null ? written : written.WithDirectives(WriteDirectives(fragment, site)));
                        break;

                    default:
                        result.Add(item);
                        break;
                }
            }

            return result;
        }

        private static List<DirectiveNode> WriteDirectives(InlineFragmentNode fragment, Site site)
        {
            var directives = new List<DirectiveNode>(fragment.Directives.Count);

            foreach (var directive in fragment.Directives)
            {
                if (!IsDeferDirective(directive))
                {
                    directives.Add(directive);
                    continue;
                }

                var arguments = directive.Arguments
                    .Where(static a => !IsInternalArgument(a))
                    .ToList();
                arguments.Add(CreateIntArgument(DocumentRewriter.DeferArguments.Group, site.Group.Number));
                arguments.Add(
                    CreateIntArgument(DocumentRewriter.DeferArguments.Parent, site.Group.Parent?.Number ?? -1));

                if (site.Levels > 0)
                {
                    arguments.Add(CreateIntArgument(DocumentRewriter.DeferArguments.Levels, site.Levels));
                }

                directives.Add(new DirectiveNode(directive.Name.Value, arguments.ToArray()));
            }

            return directives;
        }

        private static ArgumentNode CreateIntArgument(string name, int value)
            => new(name, new IntValueNode(value));

        private static bool IsInternalArgument(ArgumentNode argument)
            => argument.Name.Value is DocumentRewriter.DeferArguments.Group
                or DocumentRewriter.DeferArguments.Parent
                or DocumentRewriter.DeferArguments.Levels;

        private bool IsMarker(InlineFragmentNode fragment)
            => _sites.TryGetValue(fragment, out var site) && site.IsMarker;

        private static InlineFragmentNode CreatePlainCopy(
            InlineFragmentNode fragment,
            string variableName,
            List<ISelectionNode> content)
        {
            var skip = CreateConditionDirective(DirectiveNames.Skip.Name, variableName);
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

        private static (InlineFragmentNode Copy, InlineFragmentNode Deferred) CreateDeferredCopy(
            InlineFragmentNode fragment,
            DeferCondition condition,
            List<ISelectionNode> content)
        {
            var selectionSet = new SelectionSetNode(content);

            // An unconditional @defer has no plain copy that the deferred copy must exclude.
            if (condition.IfVariableName is not { } variableName)
            {
                var unconditional = fragment.WithSelectionSet(selectionSet);
                return (unconditional, unconditional);
            }

            var include = CreateConditionDirective(DirectiveNames.Include.Name, variableName);
            var existing = fragment.Directives.FirstOrDefault(
                static d => d.Name.Value.Equals(DirectiveNames.Include.Name, StringComparison.Ordinal));

            if (existing is null)
            {
                var directives = new List<DirectiveNode>(fragment.Directives.Count + 1);

                foreach (var directive in fragment.Directives)
                {
                    if (IsDeferDirective(directive))
                    {
                        directives.Add(include);
                    }

                    directives.Add(directive);
                }

                var switched = new InlineFragmentNode(null, fragment.TypeCondition, directives, selectionSet);
                return (switched, switched);
            }

            if (IsConditionDirective(existing, variableName))
            {
                var same = fragment.WithSelectionSet(selectionSet);
                return (same, same);
            }

            // A fragment has one @include, so the client's directives go onto an enclosing fragment.
            var defer = fragment.Directives.First(static d => IsDeferDirective(d));
            var inner = new InlineFragmentNode(null, null, [include, defer], selectionSet);
            var outer = new InlineFragmentNode(
                null,
                fragment.TypeCondition,
                fragment.Directives.Where(static d => !IsDeferDirective(d)).ToList(),
                new SelectionSetNode([inner]));
            return (outer, inner);
        }

        private static DirectiveNode CreateConditionDirective(string name, string variableName)
            => new(name, new ArgumentNode(DirectiveNames.Skip.Arguments.If, new VariableNode(variableName)));

        private static bool IsConditionDirective(DirectiveNode directive, string variableName)
            => directive.Arguments is [{ Value: VariableNode variable }]
                && variable.Name.Value.Equals(variableName, StringComparison.Ordinal);

        private static ITypeDefinition? GetFieldType(ITypeDefinition? type, FieldNode field)
            => type is IComplexTypeDefinition complexType
                && complexType.Fields.TryGetField(field.Name.Value, out var fieldDefinition)
                    ? fieldDefinition.Type.NamedType()
                    : null;

        private static bool IsListField(ITypeDefinition? type, FieldNode field)
            => type is IComplexTypeDefinition complexType
                && complexType.Fields.TryGetField(field.Name.Value, out var fieldDefinition)
                && fieldDefinition.Type.IsListType();

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
    }

    /// <summary>
    /// The state of the pruning of one deferred fragment: the parts that it cuts off, the conditions between
    /// the fragment and the current field, and whether it must be inlined.
    /// </summary>
    private sealed class PruneState(
        InlineFragmentNode fragment,
        DeferGroup group,
        string? switchVariable)
    {
        public InlineFragmentNode Fragment { get; } = fragment;

        public DeferGroup Group { get; } = group;

        public string? SwitchVariable { get; } = switchVariable;

        public List<PendingPart> Parts { get; } = [];

        public List<DirectiveNode> Conditions { get; } = [];

        /// <summary>
        /// The nested fragments that must be inlined.
        /// </summary>
        public List<InlineFragmentNode> NestedToInline { get; } = [];

        public int Unsupported { get; private set; }

        public bool MustInline { get; set; }

        public (int Conditions, int Unsupported) Push(IReadOnlyList<DirectiveNode> directives)
        {
            var mark = (Conditions.Count, Unsupported);

            foreach (var directive in directives)
            {
                if (directive.Name.Value is DirectiveNames.Skip.Name or DirectiveNames.Include.Name)
                {
                    Conditions.Add(directive);
                }
                else
                {
                    Unsupported++;
                }
            }

            return mark;
        }

        public void Pop((int Conditions, int Unsupported) mark)
        {
            Conditions.RemoveRange(mark.Conditions, Conditions.Count - mark.Conditions);
            Unsupported = mark.Unsupported;
        }
    }

    /// <summary>
    /// A fragment that a deferred fragment cuts off below a covered list, with the field of the outside
    /// selection that it is attached to.
    /// </summary>
    private sealed record PendingPart(
        Pass Target,
        FieldNode Input,
        InlineFragmentNode Fragment,
        ITypeDefinition? ItemType,
        List<ISelectionNode> Content);

    /// <summary>
    /// The delivery group of one deferred fragment. The number is assigned in the document order of the places
    /// where the fragments stand, and a parent always has a lower number than its children.
    /// </summary>
    private sealed class DeferGroup(DeferGroup? parent)
    {
        public DeferGroup? Parent { get; } = parent;

        public int Number { get; set; } = -1;
    }

    /// <summary>
    /// The group of a deferred fragment in the lowered operation, the field levels between a part and the
    /// place of its fragment, whether the fragment only marks that place, and the fragment of the operation
    /// that it was lowered from.
    /// </summary>
    private sealed record Site(DeferGroup Group, int Levels)
    {
        public bool IsMarker { get; init; }

        public InlineFragmentNode? Origin { get; init; }
    }

    /// <summary>
    /// One lowering and pruning of the content of a deferred fragment.
    /// </summary>
    private sealed record DeferAttempt(
        Scope Scope,
        List<ISelectionNode> Content,
        Coverage Coverage,
        PruneState State);

    /// <summary>
    /// One lowering of a selection list: the lowered field of every composite field, by the field it was
    /// lowered from.
    /// </summary>
    private sealed class Pass
    {
        public Dictionary<FieldNode, FieldNode> Outputs { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// The position of a selection list: the type of its fields, the lists that enclose it on the same
    /// field level, and the lowering pass and group that it belongs to.
    /// </summary>
    private sealed class Scope
    {
        private Scope(
            ITypeDefinition? type,
            bool isRoot,
            Level level,
            Pass pass,
            DeferGroup? group,
            List<PendingPart> sink)
        {
            Type = type;
            IsRoot = isRoot;
            Level = level;
            Pass = pass;
            Group = group;
            Sink = sink;
        }

        public ITypeDefinition? Type { get; }

        public bool IsRoot { get; }

        public Level Level { get; }

        public Pass Pass { get; }

        public DeferGroup? Group { get; }

        public List<PendingPart> Sink { get; }

        public static Scope Root(ITypeDefinition type, IReadOnlyList<ISelectionNode> items)
        {
            var pass = new Pass();
            return new(type, true, new Level(items, null, null, pass), pass, null, []);
        }

        public Scope EnterField(ITypeDefinition? type, IReadOnlyList<ISelectionNode> items)
            => new(type, false, new Level(items, null, null, Pass), Pass, Group, Sink);

        public Scope EnterFragment(ITypeDefinition? type, InlineFragmentNode owner, IReadOnlyList<ISelectionNode> items)
            => new(type, IsRoot, new Level(items, owner, Level, Pass), Pass, Group, Sink);

        public Scope EnterDeferred(
            ITypeDefinition? type,
            InlineFragmentNode owner,
            IReadOnlyList<ISelectionNode> items,
            DeferGroup group)
        {
            var pass = new Pass();
            return new(type, IsRoot, new Level(items, owner, Level, pass), pass, group, []);
        }

        public Scope EnterCopy(ITypeDefinition? type, InlineFragmentNode owner, IReadOnlyList<ISelectionNode> items)
        {
            var pass = new Pass();
            return new(type, IsRoot, new Level(items, owner, Level, pass), pass, Group, []);
        }
    }

    /// <summary>
    /// A selection list on one field level, with the fragment that owns it and the list that encloses it at
    /// the same level.
    /// </summary>
    private sealed class Level(
        IReadOnlyList<ISelectionNode> items,
        InlineFragmentNode? owner,
        Level? outer,
        Pass pass)
    {
        public IReadOnlyList<ISelectionNode> Items { get; } = items;

        public NamedTypeNode? OwnerTypeCondition => owner?.TypeCondition;

        public Level? Outer { get; } = outer;

        public Pass Pass { get; } = pass;

        /// <summary>
        /// Whether a selection of an outer level can take a part of a fragment that stands in this level,
        /// which holds when the owner adds no type condition and no directive but an unconditional
        /// <c>@defer</c>.
        /// </summary>
        public bool IsTransparent
        {
            get
            {
                if (owner is null)
                {
                    return true;
                }

                if (owner.TypeCondition is not null)
                {
                    return false;
                }

                foreach (var directive in owner.Directives)
                {
                    if (!directive.Name.Value.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    foreach (var argument in directive.Arguments)
                    {
                        if (argument.Name.Value.Equals(DirectiveNames.Defer.Arguments.If, StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }

    /// <summary>
    /// A field that an enclosing selection selects without conditions, the pass that lowers it, and whether
    /// a part can be attached to it.
    /// </summary>
    private sealed class CoveredField(FieldNode node, Pass pass, bool attachable)
    {
        public FieldNode Node { get; } = node;

        public Pass Pass { get; } = pass;

        public bool Attachable { get; } = attachable;

        public Coverage Child { get; } = new();
    }

    /// <summary>
    /// The fields that an enclosing selection at a place selects without conditions: outside of any deferred
    /// fragment, or as an own field of an enclosing deferred fragment.
    /// </summary>
    private sealed class Coverage
    {
        public static readonly Coverage Empty = new();

        public Dictionary<string, CoveredField> Fields { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The composite fields that an enclosing selection selects only under a condition or for some types, by
        /// response name, with what that selection selects below each of them.
        /// </summary>
        public Dictionary<string, Coverage> ConditionalFields { get; } = new(StringComparer.Ordinal);

        public static Coverage Create(Scope scope, NamedTypeNode? typeCondition)
        {
            var coverage = new Coverage();
            var typeConditions = new HashSet<string>(StringComparer.Ordinal);
            var attachable = true;

            if (typeCondition is not null)
            {
                typeConditions.Add(typeCondition.Name.Value);
            }

            for (var level = scope.Level; level is not null; level = level.Outer)
            {
                coverage.Collect(level.Items, typeConditions, level.Pass, attachable, false, false);

                if (level.OwnerTypeCondition is not null)
                {
                    typeConditions.Add(level.OwnerTypeCondition.Name.Value);
                }

                attachable &= level.IsTransparent;
            }

            return coverage;
        }

        private void Collect(
            IReadOnlyList<ISelectionNode> items,
            HashSet<string> typeConditions,
            Pass pass,
            bool attachable,
            bool typed,
            bool conditional)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FieldNode { Directives.Count: 0 } field when !conditional:
                        var name = field.Alias?.Value ?? field.Name.Value;

                        if (!Fields.TryGetValue(name, out var covered))
                        {
                            covered = new CoveredField(field, pass, attachable && !typed);
                            Fields.Add(name, covered);
                        }

                        if (field.SelectionSet is { } selectionSet)
                        {
                            covered.Child.Collect(selectionSet.Selections, [], pass, covered.Attachable, false, false);
                        }

                        break;

                    case FieldNode { SelectionSet: { } conditionalSet } field:
                        var conditionalName = field.Alias?.Value ?? field.Name.Value;

                        if (!ConditionalFields.TryGetValue(conditionalName, out var below))
                        {
                            below = new Coverage();
                            ConditionalFields.Add(conditionalName, below);
                        }

                        below.Collect(conditionalSet.Selections, [], pass, false, false, true);
                        break;

                    case InlineFragmentNode fragment
                        when fragment.Directives.Count == 0 || IsOnlyConditional(fragment):
                        var applies = fragment.TypeCondition is null
                            || typeConditions.Contains(fragment.TypeCondition.Name.Value);

                        Collect(
                            fragment.SelectionSet.Selections,
                            typeConditions,
                            pass,
                            attachable,
                            typed || fragment.TypeCondition is not null,
                            conditional || fragment.Directives.Count > 0 || !applies);
                        break;
                }
            }
        }

        private static bool IsOnlyConditional(InlineFragmentNode fragment)
        {
            foreach (var directive in fragment.Directives)
            {
                if (directive.Name.Value is not (DirectiveNames.Skip.Name or DirectiveNames.Include.Name))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
