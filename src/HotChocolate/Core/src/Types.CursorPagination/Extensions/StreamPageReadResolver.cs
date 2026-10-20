using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;

namespace HotChocolate.Types.Pagination;

internal static class StreamPageReadResolver
{
    private const string ForwardCursors = "forwardCursors";
    private const string BackwardCursors = "backwardCursors";

    public static StreamPageReads Resolve(IResolverContext context)
    {
        var selection = context.Selection;

        if (selection.Type.NamedType() is not ObjectType connectionType)
        {
            return StreamPageReads.None;
        }

        var deferFlags = context.Operation.CreateDeferConditionFlags(context.Variables);
        var connectionDeferUsage = selection.GetPrimaryDeferUsage(deferFlags);
        var reads = StreamPageReads.None;

        foreach (var child in context.GetSelections(connectionType))
        {
            if (IsDeferredOrStreamed(child, deferFlags, connectionDeferUsage))
            {
                continue;
            }

            switch (child.Field.Name)
            {
                case ConnectionType.Names.Edges:
                case ConnectionType.Names.Nodes:
                    reads |= StreamPageReads.AllRows;
                    break;

                case ConnectionType.Names.TotalCount:
                    reads |= StreamPageReads.TotalCount;
                    break;

                case ConnectionType.Names.PageInfo:
                    reads |= ResolvePageInfo(context, child, deferFlags, connectionDeferUsage);
                    break;
            }
        }

        return reads;
    }

    private static StreamPageReads ResolvePageInfo(
        IResolverContext context,
        Selection pageInfo,
        ConditionFlags deferFlags,
        DeferUsage? connectionDeferUsage)
    {
        if (pageInfo.Type.NamedType() is not ObjectType pageInfoType)
        {
            return StreamPageReads.None;
        }

        var reads = StreamPageReads.None;

        foreach (var child in context.GetSelections(pageInfoType, pageInfo))
        {
            if (IsDeferredOrStreamed(child, deferFlags, connectionDeferUsage))
            {
                continue;
            }

            switch (child.Field.Name)
            {
                case PageInfo.Names.HasPreviousPage:
                case PageInfo.Names.StartCursor:
                    reads |= StreamPageReads.FirstRow;
                    break;

                case PageInfo.Names.HasNextPage:
                case PageInfo.Names.EndCursor:
                case ForwardCursors:
                case BackwardCursors:
                    reads |= StreamPageReads.AllRows;
                    break;
            }
        }

        return reads;
    }

    private static bool IsDeferredOrStreamed(
        Selection selection,
        ConditionFlags deferFlags,
        DeferUsage? connectionDeferUsage)
    {
        if (selection.GetPrimaryDeferUsage(deferFlags) != connectionDeferUsage)
        {
            return true;
        }

        foreach (var syntaxNode in selection.SyntaxNodes)
        {
            foreach (var directive in syntaxNode.Node.Directives)
            {
                if (directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
