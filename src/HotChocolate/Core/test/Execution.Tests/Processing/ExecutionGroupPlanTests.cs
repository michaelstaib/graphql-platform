namespace HotChocolate.Execution.Processing;

public class ExecutionGroupPlanTests
{
    [Fact]
    public async Task Create_Should_OrderSlotsByPriorityThenSelectionOrder_When_SelectionsUseAliasesAndFragments()
    {
        // arrange
        var plan = await CreatePlanAsync(
            """
            {
                connection {
                    ...Connection
                    pageInfo { hasNextPage }
                    b: nodes { name }
                    ... @defer { c: nodes { name } }
                }
            }

            fragment Connection on Connection {
                a: nodes { name }
                edges
            }
            """);

        // act
        var slots = plan.Slots.Select(s => $"{s.Selection.ResponseName} {s.Priority} {s.IsStreamable}");

        // assert
        slots.MatchInlineSnapshots(
        [
            "a 0 True",
            "b 0 True",
            "c 0 True",
            "edges 1 False",
            "hasNextPage 2 False"
        ]);
    }

    [Fact]
    public async Task Create_Should_MarkSelectionsOnTheWayToMembers_When_PathPassesThroughFields()
    {
        // arrange
        var (operation, rootSet, _) = await ExecutionGroupTestOperation.CompileAsync(
            "{ connection { nodes { name } pageInfo { hasNextPage probe } probe } }");
        var pageInfo = rootSet.Selections.ToArray().Single(s => s.Field.Name is "pageInfo");

        // act
        var participants = rootSet.Selections.ToArray()
            .Concat(operation.GetSelectionSet(pageInfo).Selections.ToArray())
            .Select(s => $"{s.DeclaringType.Name}.{s.Field.Name} {s.IsExecutionGroupParticipant}");

        // assert
        participants.MatchInlineSnapshots(
        [
            "Connection.nodes True",
            "Connection.pageInfo True",
            "Connection.probe False",
            "PageInfo.hasNextPage True",
            "PageInfo.probe False"
        ]);
    }

    [Fact]
    public async Task Create_Should_KeepPureAncestorsPure_When_OnlyMembersMustRunAsTasks()
    {
        // arrange
        var (operation, rootSet, _) = await ExecutionGroupTestOperation.CompileAsync(
            "{ pureInfo { nodes { name } pageInfo { hasNextPage probe } } }");
        var pageInfo = rootSet.Selections.ToArray().Single(s => s.Field.Name is "pageInfo");

        // act
        var strategies = rootSet.Selections.ToArray()
            .Concat(operation.GetSelectionSet(pageInfo).Selections.ToArray())
            .Select(s => $"{s.DeclaringType.Name}.{s.Field.Name} {s.Strategy}");

        // assert
        strategies.MatchInlineSnapshots(
        [
            "PureInfoConnection.nodes Default",
            "PureInfoConnection.pageInfo Pure",
            "PageInfo.hasNextPage Default",
            "PageInfo.probe Default"
        ]);
    }

    private static async Task<ExecutionGroupPlan> CreatePlanAsync(string query)
    {
        var (_, _, plan) = await ExecutionGroupTestOperation.CompileAsync(query);
        return plan;
    }
}
