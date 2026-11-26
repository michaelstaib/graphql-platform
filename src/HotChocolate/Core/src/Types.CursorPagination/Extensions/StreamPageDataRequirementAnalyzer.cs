using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace HotChocolate.Types.Pagination;

internal static class StreamPageDataRequirementAnalyzer
{
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

        var deferFlags = context.Operation.CreateDeferConditionFlags(context.Variables);
        var connectionDeferUsage = selection.GetPrimaryDeferUsage(deferFlags);
        var requirements = StreamPageDataRequirements.None;

        foreach (var child in context.GetSelections(connectionType))
        {
            if (IsDeferredOrStreamed(child, context.Variables, deferFlags, connectionDeferUsage))
            {
                continue;
            }

            requirements |= child.Field.Name.Equals(ConnectionType.Names.PageInfo, StringComparison.Ordinal)
                ? AnalyzePageInfo(context, child, deferFlags, connectionDeferUsage)
                : StreamPageConnectionExecutionGroup.GetConnectionRequirements(child.Field.Name);
        }

        return requirements;
    }

    private static StreamPageDataRequirements AnalyzePageInfo(
        IResolverContext context,
        Selection pageInfo,
        ConditionFlags deferFlags,
        DeferUsage? connectionDeferUsage)
    {
        if (pageInfo.Type.NamedType() is not ObjectType pageInfoType)
        {
            return StreamPageDataRequirements.None;
        }

        var requirements = StreamPageDataRequirements.None;

        foreach (var child in context.GetSelections(pageInfoType, pageInfo))
        {
            if (IsDeferredOrStreamed(child, context.Variables, deferFlags, connectionDeferUsage))
            {
                continue;
            }

            requirements |= StreamPageConnectionExecutionGroup.GetPageInfoRequirements(child.Field.Name);
        }

        return requirements;
    }

    private static bool IsDeferredOrStreamed(
        Selection selection,
        IVariableValueCollection variables,
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
                if (directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal)
                    && IsStreamEnabled(directive, variables))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsStreamEnabled(DirectiveNode directive, IVariableValueCollection variables)
        => DirectiveIfCondition.IsEnabled(
            DirectiveNames.Stream.Name,
            directive.GetArgumentValue(DirectiveNames.Stream.Arguments.If),
            variables,
            defaultValue: true);
}
