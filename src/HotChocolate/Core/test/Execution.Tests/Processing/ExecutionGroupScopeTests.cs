using System.Text;

namespace HotChocolate.Execution.Processing;

public class ExecutionGroupScopeTests
{
    private const string Query = "{ connection { nodes { name } edges pageInfo { hasNextPage } } }";

    [Fact]
    public async Task Open_Should_StartAtTheFirstSlot_When_AllMembersAreIncluded()
    {
        // arrange
        var plan = await CreatePlanAsync(Query);

        // act
        var scope = ExecutionGroupScope.Open(plan, new ConditionFlags(ulong.MaxValue, null));

        // assert
        Assert.Equal(0, scope!.Position);
    }

    [Fact]
    public async Task Open_Should_ReturnNull_When_FewerThanTwoMembersAreIncluded()
    {
        // arrange
        var plan = await CreatePlanAsync(
            "query ($skip: Boolean!) { connection { nodes @skip(if: $skip) { name } edges } }");

        // act
        var scope = ExecutionGroupScope.Open(plan, default);

        // assert
        Assert.Null(scope);
    }

    [Fact]
    public async Task Open_Should_SkipMembers_When_RequestExcludesThem()
    {
        // arrange
        var plan = await CreatePlanAsync(
            """
            query ($skip: Boolean!) {
                connection {
                    nodes @skip(if: $skip) { name }
                    edges
                    pageInfo { hasNextPage }
                }
            }
            """);

        // act
        var scope = ExecutionGroupScope.Open(plan, default);

        // assert
        Assert.Equal(1, scope!.Position);
    }

    [Fact]
    public async Task Open_Should_SkipMembers_When_AncestorIsExcluded()
    {
        // arrange
        var plan = await CreatePlanAsync(
            """
            query ($skip: Boolean!) {
                reversed {
                    pageInfo @skip(if: $skip) { hasNextPage }
                    nodes { name }
                    edges
                }
            }
            """);

        // act
        var scope = ExecutionGroupScope.Open(plan, default);

        // assert
        Assert.Equal(1, scope!.Position);
    }

    [Fact]
    public async Task Release_Should_MoveToTheNextSlot_When_TheCurrentSlotIsReleased()
    {
        // arrange
        var scope = await OpenAsync(Query);
        var positions = new List<int> { scope.Position };

        // act
        scope.Release(0);
        positions.Add(scope.Position);
        scope.Release(1);
        positions.Add(scope.Position);
        scope.Release(2);
        positions.Add(scope.Position);

        // assert
        positions.Select(p => p.ToString()).MatchInlineSnapshots(["0", "1", "2", "3"]);
    }

    [Fact]
    public async Task Release_Should_KeepThePosition_When_ALaterSlotIsReleasedBeforeItsTurn()
    {
        // arrange
        var scope = await OpenAsync(Query);
        var positions = new List<int>();

        // act
        scope.Release(2);
        positions.Add(scope.Position);
        scope.Release(0);
        positions.Add(scope.Position);
        scope.Release(1);
        positions.Add(scope.Position);

        // assert
        positions.Select(p => p.ToString()).MatchInlineSnapshots(["0", "1", "3"]);
    }

    [Fact]
    public async Task Release_Should_BeIdempotent_When_SlotIsReleasedTwice()
    {
        // arrange
        var scope = await OpenAsync(Query);

        // act
        scope.Release(0);
        scope.Release(0);

        // assert
        Assert.Equal(1, scope.Position);
    }

    [Fact]
    public async Task Release_Should_DispatchStoredTask_When_ItsSlotIsReleasedBeforeItsTurn()
    {
        // arrange
        var scope = await OpenAsync(Query);
        var task = new RecordingTask(slot: 2);
        scope.Submit(task);
        var callsAfterSubmit = task.Calls.ToArray();

        // act
        scope.Release(2);

        // assert
        callsAfterSubmit.MatchInlineSnapshots(["Reserve"]);
        task.Calls.MatchInlineSnapshots(["Reserve", "RegisterReserved"]);
    }

    [Fact]
    public async Task Release_Should_DispatchStoredTaskOnce_When_ItsSlotBecomesTheCurrentPosition()
    {
        // arrange
        var scope = await OpenAsync(Query);
        var task = new RecordingTask(slot: 1);
        scope.Submit(task);

        // act
        scope.Release(1);
        scope.Release(0);

        // assert
        task.Calls.MatchInlineSnapshots(["Reserve", "RegisterReserved"]);
    }

    [Fact]
    public async Task Submit_Should_RegisterTaskDirectly_When_ItIsTheTurnOfItsSlot()
    {
        // arrange
        var scope = await OpenAsync(Query);
        var task = new RecordingTask(slot: 0);

        // act
        scope.Submit(task);

        // assert
        task.Calls.MatchInlineSnapshots(["Register"]);
    }

    [Fact]
    public async Task ReleaseDescendants_Should_SkipMembersBelowTheSelection_When_NoChildTaskWasCreated()
    {
        // arrange
        var (_, rootSet, plan) = await ExecutionGroupTestOperation.CompileAsync(Query);
        var scope = ExecutionGroupScope.Open(plan, new ConditionFlags(ulong.MaxValue, null))!;
        var pageInfo = rootSet.Selections.ToArray().Single(s => s.Field.Name is "pageInfo");
        plan.TryGetNode(pageInfo.Id, out var node);
        scope.Release(0);
        scope.Release(1);

        // act
        scope.ReleaseDescendants(node!);

        // assert
        Assert.Equal(3, scope.Position);
    }

    [Fact]
    public async Task Abandon_Should_MoveBehindTheLastSlot_When_ScopeIsAbandoned()
    {
        // arrange
        var scope = await OpenAsync(Query);

        // act
        scope.Abandon();

        // assert
        Assert.Equal(3, scope.Position);
    }

    [Fact]
    public async Task Release_Should_AdvanceAcrossWords_When_ScopeHasMoreThan64Slots()
    {
        // arrange
        var builder = new StringBuilder("{ connection {");

        for (var i = 0; i < 70; i++)
        {
            builder.Append($" a{i}: nodes {{ name }}");
        }

        builder.Append(" } }");
        var scope = await OpenAsync(builder.ToString());
        var positions = new List<int>();

        // act
        scope.Release(65);

        for (var i = 0; i < 64; i++)
        {
            scope.Release(i);
        }

        positions.Add(scope.Position);
        scope.Release(64);
        positions.Add(scope.Position);

        for (var i = 66; i < 70; i++)
        {
            scope.Release(i);
        }

        positions.Add(scope.Position);

        // assert
        positions.Select(p => p.ToString()).MatchInlineSnapshots(["64", "66", "70"]);
    }

    private static async Task<ExecutionGroupScope> OpenAsync(string query)
    {
        var plan = await CreatePlanAsync(query);
        return ExecutionGroupScope.Open(plan, new ConditionFlags(ulong.MaxValue, null))!;
    }

    private static async Task<ExecutionGroupPlan> CreatePlanAsync(string query)
    {
        var (_, _, plan) = await ExecutionGroupTestOperation.CompileAsync(query);
        return plan;
    }

    private sealed class RecordingTask(int slot) : IExecutionGroupTask
    {
        public List<string> Calls { get; } = [];

        public int GroupSlot => slot;

        public void Register() => Calls.Add(nameof(Register));

        public void Reserve() => Calls.Add(nameof(Reserve));

        public void RegisterReserved() => Calls.Add(nameof(RegisterReserved));
    }
}
