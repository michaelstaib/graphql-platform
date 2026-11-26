using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;

namespace HotChocolate.Types.Pagination;

internal static class StreamPageDataRequirementAnalyzer
{
    /// <summary>
    /// Gets the data the connection must hold in memory before its resolver returns, and the number of leading
    /// rows the streamed members deliver with the connection.
    /// </summary>
    public static StreamPageDataRequirements Analyze(IResolverContext context, out int initialRowCount)
    {
        initialRowCount = 0;
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
        var selectionSet = selection.GetSelectionSet(connectionType);

        if (selection.Features.TryGet(out precomputed))
        {
            return precomputed;
        }

        var variables = context.Variables;
        var deferFlags = context.Operation.CreateDeferConditionFlags(variables);
        var connectionDeferUsage = selection.GetPrimaryDeferUsage(deferFlags);
        var plan = selectionSet.ExecutionGroupPlan;
        var honorsStreams = plan?.HonorsStreams(
            context.IncludeConditionFlags,
            deferFlags,
            connectionDeferUsage,
            variables) == true;
        var requirements = StreamPageDataRequirements.None;

        foreach (var child in context.GetSelections(connectionType))
        {
            if (child.GetPrimaryDeferUsage(deferFlags) != connectionDeferUsage)
            {
                continue;
            }

            if (honorsStreams
                && child.IsStreamable
                && child.StreamArguments is { } stream
                && plan!.TryGetNode(child.Id, out var node)
                && node.SlotIndex >= 0)
            {
                initialRowCount = Math.Max(initialRowCount, stream.GetInitialCount(variables));
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
            if (child.GetPrimaryDeferUsage(deferFlags) != connectionDeferUsage)
            {
                continue;
            }

            requirements |= StreamPageConnectionExecutionGroup.GetPageInfoRequirements(child.Field.Name);
        }

        return requirements;
    }
}
