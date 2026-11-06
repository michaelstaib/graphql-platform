using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace HotChocolate.Types.Pagination;

internal static class StreamPageDataRequirementAnalyzer
{
    private const string ForwardCursors = "forwardCursors";
    private const string BackwardCursors = "backwardCursors";

    /// <summary>
    /// Gets the requirements of a connection field other than <c>pageInfo</c>.
    /// </summary>
    public static StreamPageDataRequirements GetConnectionRequirements(string fieldName)
        => fieldName switch
        {
            ConnectionType.Names.Edges or ConnectionType.Names.Nodes => StreamPageDataRequirements.AllRows,
            ConnectionType.Names.TotalCount => StreamPageDataRequirements.TotalCount,
            _ => StreamPageDataRequirements.None
        };

    /// <summary>
    /// Gets the requirements of a <c>pageInfo</c> field.
    /// </summary>
    public static StreamPageDataRequirements GetPageInfoRequirements(string fieldName)
        => fieldName switch
        {
            PageInfo.Names.HasPreviousPage or PageInfo.Names.StartCursor => StreamPageDataRequirements.FirstRow,
            PageInfo.Names.HasNextPage or PageInfo.Names.EndCursor or ForwardCursors or BackwardCursors
                => StreamPageDataRequirements.AllRows,
            _ => StreamPageDataRequirements.None
        };

    public static StreamPageDataRequirements Analyze(IResolverContext context)
    {
        var selection = context.Selection;

        if (selection.Type.NamedType() is not ObjectType connectionType)
        {
            return StreamPageDataRequirements.None;
        }

        if (selection.Features.TryGet<StreamPageDataRequirements>(out var precomputed))
        {
            return precomputed;
        }

        // The requirements of an unconditional selection are stored while its selection set is compiled.
        selection.GetSelectionSet(connectionType);

        if (selection.Features.TryGet(out precomputed))
        {
            return precomputed;
        }

        var scope = CreateDeferScope(context);
        var requirements = StreamPageDataRequirements.None;

        foreach (var child in context.GetSelections(connectionType))
        {
            if (IsDeferredOrStreamed(child, context.Variables, scope))
            {
                continue;
            }

            requirements |= child.Field.Name.Equals(ConnectionType.Names.PageInfo, StringComparison.Ordinal)
                ? AnalyzePageInfo(context, child, scope)
                : GetConnectionRequirements(child.Field.Name);
        }

        return requirements;
    }

    private static StreamPageDataRequirements AnalyzePageInfo(
        IResolverContext context,
        Selection pageInfo,
        DeferScope scope)
    {
        if (pageInfo.Type.NamedType() is not ObjectType pageInfoType)
        {
            return StreamPageDataRequirements.None;
        }

        var requirements = StreamPageDataRequirements.None;

        foreach (var child in context.GetSelections(pageInfoType, pageInfo))
        {
            if (IsDeferredOrStreamed(child, context.Variables, scope))
            {
                continue;
            }

            requirements |= GetPageInfoRequirements(child.Field.Name);
        }

        return requirements;
    }

    private static bool IsDeferredOrStreamed(
        Selection selection,
        IVariableValueCollection variables,
        DeferScope scope)
    {
        if (scope.IsDeferred(selection))
        {
            return true;
        }

        foreach (var syntaxNode in selection.SyntaxNodes)
        {
            foreach (var directive in syntaxNode.Node.Directives)
            {
                if (directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal)
                    && IsStreamEnabled(directive, variables))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // the connection's children are completed in the execution branch of the connection field. Without the
    // execution context nothing counts as deferred, which only widens the requirements.
    private static DeferScope CreateDeferScope(IResolverContext context)
    {
        if (context is not MiddlewareContext middlewareContext)
        {
            return default;
        }

        return new DeferScope(
            middlewareContext.DeferUsageSetTable,
            middlewareContext.Operation.CreateDeferConditionFlags(middlewareContext.Variables),
            middlewareContext.DeferUsage);
    }

    private readonly record struct DeferScope(
        DeferUsageSetTable? Table,
        ConditionFlags DeferFlags,
        DeferUsage? BranchDeferUsage)
    {
        // a selection is deferred out of the branch when it has an active usage set that does not include
        // the usage of the branch.
        public bool IsDeferred(Selection selection)
            => Table?.GetActive(selection) is not null
                && (BranchDeferUsage is null
                    || !selection.HasActiveDeferUsage(DeferFlags.Word0, DeferFlags.Overflow, BranchDeferUsage));
    }

    internal static bool IsStreamEnabled(DirectiveNode directive, IVariableValueCollection variables)
        => DirectiveIfCondition.IsEnabled(
            DirectiveNames.Stream.Name,
            directive.GetArgumentValue(DirectiveNames.Stream.Arguments.If),
            variables,
            defaultValue: true);
}
