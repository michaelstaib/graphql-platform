using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Types;

namespace HotChocolate.Fusion.Execution.Nodes;

/// <summary>
/// Represents a plan for fields associated with a delivery group set.
/// </summary>
public sealed class IncrementalPlan : IOperationPlan
{
    /// <summary>
    /// The <see cref="ParentNodeId"/> of a plan anchored at the operation root of an
    /// enclosing plan that has no execution node.
    /// </summary>
    public const int NoParentNodeId = 0;

    private readonly ExecutionNode?[] _nodesById;

    /// <summary>
    /// Initializes a new instance of <see cref="IncrementalPlan"/>.
    /// </summary>
    /// <param name="operation">
    /// The GraphQL operation associated with this incremental plan.
    /// </param>
    /// <param name="rootNodes">
    /// The root execution nodes that serve as entry points for this
    /// incremental plan.
    /// </param>
    /// <param name="allNodes">
    /// All execution nodes belonging to this incremental plan.
    /// </param>
    /// <param name="deliveryGroups">
    /// The delivery group set associated with this incremental plan, sorted
    /// ascending by <see cref="DeliveryGroup.Id"/>.
    /// </param>
    /// <param name="requirements">
    /// The requirements that this plan resolves from its enclosing plan scope
    /// before it executes. A requirement that names a leaf field of this plan that no node of the
    /// plan reads is delivered as that field.
    /// </param>
    public IncrementalPlan(
        Operation operation,
        ImmutableArray<ExecutionNode> rootNodes,
        ImmutableArray<ExecutionNode> allNodes,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        ImmutableArray<OperationRequirement> requirements)
        : this(
            operation,
            rootNodes,
            allNodes,
            deliveryGroups,
            requirements,
            DeriveHeldFields(operation, allNodes, requirements))
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="IncrementalPlan"/> with the held fields of the plan.
    /// </summary>
    /// <param name="operation">The GraphQL operation associated with this incremental plan.</param>
    /// <param name="rootNodes">The root execution nodes of this incremental plan.</param>
    /// <param name="allNodes">All execution nodes belonging to this incremental plan.</param>
    /// <param name="deliveryGroups">The delivery group set, sorted ascending by <see cref="DeliveryGroup.Id"/>.</param>
    /// <param name="requirements">The requirements that this plan resolves from its enclosing plan scope.</param>
    /// <param name="heldFields">
    /// The requirements that name a leaf field of this plan that the enclosing scope fetched.
    /// </param>
    internal IncrementalPlan(
        Operation operation,
        ImmutableArray<ExecutionNode> rootNodes,
        ImmutableArray<ExecutionNode> allNodes,
        ImmutableArray<DeliveryGroup> deliveryGroups,
        ImmutableArray<OperationRequirement> requirements,
        ImmutableArray<OperationRequirement> heldFields)
    {
        Operation = operation;
        RootNodes = rootNodes;
        AllNodes = allNodes;
        DeliveryGroups = deliveryGroups;
        Requirements = requirements.IsDefault ? [] : requirements;
        HeldFields = heldFields.IsDefault ? [] : heldFields;
        _nodesById = CreateNodeLookup(
            allNodes,
            out var usesDynamicSchemaNames,
            out var usesBatchNodes);
        UsesDynamicSchemaNames = usesDynamicSchemaNames;
        UsesBatchNodes = usesBatchNodes;
    }

    /// <summary>
    /// Selects the requirements that name a leaf field of the plan by their key, that no node of the plan
    /// reads, and that <paramref name="operation"/> selects at the requirement path.
    /// </summary>
    internal static ImmutableArray<OperationRequirement> DeriveHeldFields(
        Operation operation,
        ImmutableArray<ExecutionNode> nodes,
        ImmutableArray<OperationRequirement> requirements)
    {
        if (requirements.IsDefaultOrEmpty)
        {
            return [];
        }

        ImmutableArray<OperationRequirement>.Builder? heldFields = null;

        foreach (var requirement in requirements)
        {
            if (requirement.InternalAlias is null
                && requirement.Map.ToString().Equals(requirement.Key, StringComparison.Ordinal)
                && !IsReadByNode(nodes, requirement.Key)
                && GetSelectionSet(operation, requirement.Path)?.TryGetSelection(requirement.Key, out _) == true)
            {
                (heldFields ??= ImmutableArray.CreateBuilder<OperationRequirement>()).Add(requirement);
            }
        }

        return heldFields?.ToImmutable() ?? [];
    }

    private static bool IsReadByNode(ImmutableArray<ExecutionNode> nodes, string key)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case OperationExecutionNode operationNode when ContainsKey(operationNode.Requirements, key):
                case ApolloOperationExecutionNode apolloNode when ContainsKey(apolloNode.Requirements, key):
                    return true;

                case OperationBatchExecutionNode batchNode:
                    foreach (var definition in batchNode.Operations)
                    {
                        if (ContainsKey(definition.Requirements, key))
                        {
                            return true;
                        }
                    }
                    break;

                case ApolloOperationBatchExecutionNode apolloBatchNode:
                    foreach (var definition in apolloBatchNode.Operations)
                    {
                        if (ContainsKey(definition.Requirements, key))
                        {
                            return true;
                        }
                    }
                    break;
            }
        }

        return false;

        static bool ContainsKey(ReadOnlySpan<OperationRequirement> requirements, string key)
        {
            foreach (var requirement in requirements)
            {
                if (requirement.Key.Equals(key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static SelectionSet? GetSelectionSet(Operation operation, SelectionPath path)
    {
        var selectionSet = operation.RootSelectionSet;
        Selection? selection = null;

        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            switch (segment.Kind)
            {
                case SelectionPathSegmentKind.Root:
                    continue;

                case SelectionPathSegmentKind.InlineFragment:
                    if (selection is null
                        || !operation.Schema.Types.TryGetType<IObjectTypeDefinition>(segment.Name, out var objectType))
                    {
                        return null;
                    }

                    selectionSet = operation.GetSelectionSet(selection, objectType);
                    continue;

                default:
                    if (selectionSet is null || !selectionSet.TryGetSelection(segment.Name, out selection))
                    {
                        return null;
                    }

                    selectionSet = selection.Type.NamedType() is IObjectTypeDefinition namedType
                        ? operation.GetSelectionSet(selection, namedType)
                        : null;
                    continue;
            }
        }

        return selectionSet;
    }

    /// <summary>
    /// Gets the unique identifier for this incremental plan.
    /// </summary>
    public string Id { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the GraphQL operation associated with this incremental plan.
    /// </summary>
    public Operation Operation { get; }

    /// <summary>
    /// Gets the root execution nodes that serve as entry points for this
    /// incremental plan.
    /// </summary>
    public ImmutableArray<ExecutionNode> RootNodes { get; }

    /// <summary>
    /// Gets all execution nodes belonging to this incremental plan.
    /// </summary>
    public ImmutableArray<ExecutionNode> AllNodes { get; }

    /// <summary>
    /// Gets the delivery group set associated with this incremental plan,
    /// sorted ascending by <see cref="DeliveryGroup.Id"/>.
    /// </summary>
    public ImmutableArray<DeliveryGroup> DeliveryGroups { get; }

    /// <summary>
    /// Gets the requirements that this plan resolves from its enclosing plan
    /// scope before it executes. Each requirement maps a variable used by this
    /// plan to a selection in the enclosing scope's result.
    /// </summary>
    public ImmutableArray<OperationRequirement> Requirements { get; }

    /// <summary>
    /// Gets the requirements that name a leaf field of this plan that the enclosing scope fetched.
    /// Their values are delivered as the fields of the same name.
    /// </summary>
    internal ImmutableArray<OperationRequirement> HeldFields { get; }

    /// <summary>
    /// Gets the <see cref="ExecutionNode.Id"/> of the node that produces the
    /// result object where this incremental plan is anchored. The identifier is
    /// scoped to the root <see cref="OperationPlan"/> for top-level plans, or
    /// to the enclosing <see cref="IncrementalPlan"/> for nested plans. The value is
    /// <see cref="NoParentNodeId"/> when the enclosing plan has no execution node.
    /// </summary>
    public int ParentNodeId { get; internal set; }

    /// <summary>
    /// Gets the highest plan node identifier that can be resolved by this plan.
    /// </summary>
    public int MaxNodeId => _nodesById.Length > 0 ? _nodesById.Length - 1 : 0;

    internal bool UsesDynamicSchemaNames { get; }

    internal bool UsesBatchNodes { get; }

    /// <summary>
    /// Gets the child incremental plans for this plan. Incremental plans do not
    /// contain child plan objects; the root <see cref="OperationPlan"/> exposes
    /// the flat collection, and deferred fragment nesting is represented by
    /// <see cref="DeliveryGroup.Parent"/>.
    /// </summary>
    public ImmutableArray<IncrementalPlan> IncrementalPlans => [];

    /// <summary>
    /// Retrieves the execution node associated with a plan node identifier.
    /// </summary>
    /// <param name="id">
    /// The identifier of an execution node, or of an operation definition inside
    /// a batch.
    /// </param>
    /// <returns>The execution node associated with the specified identifier.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no node with the specified ID exists.</exception>
    public ExecutionNode GetNodeById(int id)
    {
        if ((uint)id < (uint)_nodesById.Length
            && _nodesById[id] is { } node)
        {
            return node;
        }

        throw ThrowHelper.NodeNotFound(id);
    }

    /// <summary>
    /// Returns the <see cref="ExecutionNode"/> responsible for executing the
    /// given plan node. If the plan node is already an execution node it is
    /// returned directly; if it is an operation definition inside a batch, the
    /// containing batch node is returned.
    /// </summary>
    public ExecutionNode GetExecutionNode(IOperationPlanNode planNode)
    {
        if (planNode is ExecutionNode executionNode)
        {
            return executionNode;
        }

        if ((uint)planNode.Id < (uint)_nodesById.Length
            && _nodesById[planNode.Id] is { } node)
        {
            return node;
        }

        throw ThrowHelper.NodeNotFound(planNode.Id);
    }

    private static ExecutionNode?[] CreateNodeLookup(
        ImmutableArray<ExecutionNode> allNodes,
        out bool usesDynamicSchemaNames,
        out bool usesBatchNodes)
    {
        usesDynamicSchemaNames = false;
        usesBatchNodes = false;

        if (allNodes.IsDefaultOrEmpty)
        {
            return [];
        }

        var maxId = 0;

        foreach (var node in allNodes)
        {
            maxId = Math.Max(maxId, node.Id);

            switch (node.Type)
            {
                case ExecutionNodeType.Node:
                    usesDynamicSchemaNames = true;
                    break;

                case ExecutionNodeType.OperationBatch:
                    usesBatchNodes = true;
                    break;
            }

            if (node is OperationBatchExecutionNode batchNode)
            {
                foreach (var op in batchNode.Operations)
                {
                    maxId = Math.Max(maxId, op.Id);
                }
            }

            if (node is ApolloOperationBatchExecutionNode apolloBatchNode)
            {
                foreach (var op in apolloBatchNode.Operations)
                {
                    maxId = Math.Max(maxId, op.Id);
                }
            }
        }

        var nodesById = new ExecutionNode?[maxId + 1];

        foreach (var node in allNodes)
        {
            nodesById[node.Id] = node;

            // Map each operation definition ID to the containing batch node,
            // so GetNodeById can resolve definition IDs to execution nodes.
            if (node is OperationBatchExecutionNode batchNode)
            {
                foreach (var op in batchNode.Operations)
                {
                    nodesById[op.Id] = batchNode;
                }
            }

            if (node is ApolloOperationBatchExecutionNode apolloBatchNode)
            {
                foreach (var op in apolloBatchNode.Operations)
                {
                    nodesById[op.Id] = apolloBatchNode;
                }
            }
        }

        return nodesById;
    }
}
