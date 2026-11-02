using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Language;

namespace HotChocolate.Fusion.Execution.Nodes;

public sealed partial record OperationPlan
{
    private readonly Dictionary<IncrementalPlan, ResultSelectionSet[]>? _keyFetches;

    /// <summary>
    /// Gets the fetches of the scopes enclosing <paramref name="incrementalPlan"/> that supply its
    /// requirements on the objects it is anchored at, directly or through the requirements of another
    /// fetch. Empty when <paramref name="incrementalPlan"/> has no such fetch.
    /// </summary>
    internal ReadOnlySpan<ResultSelectionSet> GetKeyFetches(IncrementalPlan incrementalPlan)
        => _keyFetches is not null && _keyFetches.TryGetValue(incrementalPlan, out var keyFetches)
            ? keyFetches
            : [];

    private static Dictionary<IncrementalPlan, ResultSelectionSet[]>? CreateKeyFetches(
        ImmutableArray<ExecutionNode> allNodes,
        ImmutableArray<IncrementalPlan> incrementalPlans)
    {
        if (incrementalPlans.IsDefaultOrEmpty)
        {
            return null;
        }

        var keyFetches = new Dictionary<IncrementalPlan, ResultSelectionSet[]>();
        var suppliers = new HashSet<ScopeFetch>();

        foreach (var incrementalPlan in incrementalPlans)
        {
            if (incrementalPlan.Requirements.IsEmpty)
            {
                continue;
            }

            var scope = CollectEnclosingFetches(allNodes, incrementalPlan, incrementalPlans);

            foreach (var requirement in incrementalPlan.Requirements)
            {
                AddSuppliers(scope, requirement.Path, requirement, suppliers);
            }

            if (suppliers.Count > 0)
            {
                keyFetches.Add(incrementalPlan, [.. suppliers.Select(static t => t.ResultSelectionSet)]);
                suppliers.Clear();
            }
        }

        return keyFetches;
    }

    private static List<ScopeFetch> CollectEnclosingFetches(
        ImmutableArray<ExecutionNode> allNodes,
        IncrementalPlan incrementalPlan,
        ImmutableArray<IncrementalPlan> incrementalPlans)
    {
        var fetches = new List<ScopeFetch>();
        AddFetches(allNodes, fetches);

        foreach (var candidate in incrementalPlans)
        {
            if (!ReferenceEquals(candidate, incrementalPlan) && Encloses(candidate, incrementalPlan))
            {
                AddFetches(candidate.AllNodes, fetches);
            }
        }

        return fetches;
    }

    private static bool Encloses(IncrementalPlan candidate, IncrementalPlan incrementalPlan)
    {
        foreach (var deliveryGroup in incrementalPlan.DeliveryGroups)
        {
            for (var ancestor = deliveryGroup.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (candidate.DeliveryGroups.Contains(ancestor))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AddFetches(ImmutableArray<ExecutionNode> nodes, List<ScopeFetch> fetches)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case OperationExecutionNode operation:
                    fetches.Add(new ScopeFetch(
                        [operation.Target],
                        operation.ResultSelectionSet,
                        operation.Requirements.ToArray()));
                    break;

                case ApolloOperationExecutionNode operation:
                    fetches.Add(new ScopeFetch(
                        [operation.Target],
                        operation.ResultSelectionSet,
                        operation.Requirements.ToArray()));
                    break;

                case OperationBatchExecutionNode batch:
                    AddFetches(batch.Operations, fetches);
                    break;

                case ApolloOperationBatchExecutionNode batch:
                    AddFetches(batch.Operations, fetches);
                    break;
            }
        }
    }

    private static void AddFetches(ReadOnlySpan<OperationDefinition> operations, List<ScopeFetch> fetches)
    {
        foreach (var operation in operations)
        {
            fetches.Add(new ScopeFetch(
                operation is BatchOperationDefinition batch
                    ? batch.Targets.ToArray()
                    : [((SingleOperationDefinition)operation).Target],
                operation.ResultSelectionSet,
                operation.Requirements.ToArray()));
        }
    }

    private static void AddSuppliers(
        List<ScopeFetch> scope,
        SelectionPath anchor,
        OperationRequirement requirement,
        HashSet<ScopeFetch> suppliers)
    {
        foreach (var fetch in scope)
        {
            if (!fetch.Targets(anchor) || !fetch.Selects(requirement) || !suppliers.Add(fetch))
            {
                continue;
            }

            foreach (var fetchRequirement in fetch.Requirements)
            {
                AddSuppliers(scope, anchor, fetchRequirement, suppliers);
            }
        }
    }

    private sealed class ScopeFetch(
        SelectionPath[] targets,
        ResultSelectionSet resultSelectionSet,
        OperationRequirement[] requirements)
    {
        public ResultSelectionSet ResultSelectionSet { get; } = resultSelectionSet;

        public OperationRequirement[] Requirements { get; } = requirements;

        public bool Targets(SelectionPath anchor)
        {
            foreach (var target in targets)
            {
                if (target.Equals(anchor))
                {
                    return true;
                }
            }

            return false;
        }

        public bool Selects(OperationRequirement requirement)
            => requirement.InternalAlias is { } alias
                ? SelectsField(alias)
                : Selects(requirement.Map);

        private bool Selects(IValueSelectionNode? map)
        {
            switch (map)
            {
                case PathNode path:
                    return SelectsField(path.PathSegment.FieldName.Value);

                case PathObjectValueSelectionNode path:
                    return Selects(path.Path);

                case PathListValueSelectionNode path:
                    return Selects(path.Path);

                case ListValueSelectionNode list:
                    return Selects(list.ElementSelection);

                case ChoiceValueSelectionNode choice:
                    foreach (var branch in choice.Branches)
                    {
                        if (Selects(branch))
                        {
                            return true;
                        }
                    }

                    return false;

                case ObjectValueSelectionNode value:
                    foreach (var field in value.Fields)
                    {
                        var selects = field.ValueSelection is null
                            ? SelectsField(field.Name.Value)
                            : Selects(field.ValueSelection);

                        if (selects)
                        {
                            return true;
                        }
                    }

                    return false;

                default:
                    return false;
            }
        }

        private bool SelectsField(string responseName)
            => ResultSelectionSet.ResponseNames.Contains(responseName);
    }
}
