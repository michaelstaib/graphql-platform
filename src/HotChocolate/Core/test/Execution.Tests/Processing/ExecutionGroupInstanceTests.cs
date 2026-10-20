namespace HotChocolate.Execution.Processing;

public class ExecutionGroupInstanceTests
{
    private const string Query = "{ connection { nodes { name } edges pageInfo { hasNextPage } } }";

    [Fact]
    public async Task WaitForTurnAsync_Should_CompleteImmediately_When_MemberIsFirstInSequence()
    {
        // arrange
        var (instance, nodes) = await OpenAsync(Query);

        // act
        var wait = instance.WaitForTurnAsync(nodes[0], TestContext.Current.CancellationToken);

        // assert
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitForTurnAsync_Should_CompleteInSequence_When_MembersAreReadyOutOfOrder()
    {
        // arrange
        var (instance, nodes) = await OpenAsync(Query);
        var token = TestContext.Current.CancellationToken;
        var third = instance.WaitForTurnAsync(nodes[2], token).AsTask();
        var second = instance.WaitForTurnAsync(nodes[1], token).AsTask();
        var states = new List<string> { Describe(second, third) };

        // act
        instance.Complete(nodes[0], releaseDescendants: false);
        await second.WaitAsync(TimeSpan.FromSeconds(15), token);
        states.Add(Describe(second, third));
        instance.Complete(nodes[1], releaseDescendants: false);
        await third.WaitAsync(TimeSpan.FromSeconds(15), token);
        states.Add(Describe(second, third));

        // assert
        states.MatchInlineSnapshots(
        [
            "second=False third=False",
            "second=True third=False",
            "second=True third=True"
        ]);
    }

    [Fact]
    public async Task WaitForTurnAsync_Should_CompleteWaiter_When_ItsSlotIsReleasedBeforeItsTurn()
    {
        // arrange
        var (instance, nodes) = await OpenAsync(Query);
        var token = TestContext.Current.CancellationToken;
        var pageInfo = nodes[3];
        var third = instance.WaitForTurnAsync(nodes[2], token).AsTask();

        // act
        instance.Complete(pageInfo, releaseDescendants: true);
        var pendingBeforePredecessors = !third.IsCompleted;
        instance.Complete(nodes[0], releaseDescendants: false);
        instance.Complete(nodes[1], releaseDescendants: false);
        await third.WaitAsync(TimeSpan.FromSeconds(15), token);

        // assert
        Assert.True(pendingBeforePredecessors);
        Assert.True(third.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitForTurnAsync_Should_CompleteImmediately_When_MembersBelowSelectionAreReleased()
    {
        // arrange
        var (instance, nodes) = await OpenAsync(Query);
        var pageInfo = nodes[3];
        instance.Complete(nodes[0], releaseDescendants: false);
        instance.Complete(nodes[1], releaseDescendants: false);

        // act
        instance.Complete(pageInfo, releaseDescendants: true);
        var wait = instance.WaitForTurnAsync(nodes[2], TestContext.Current.CancellationToken);

        // assert
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Open_Should_ReleaseMembers_When_RequestExcludesThem()
    {
        // arrange
        var plan = await CreatePlanAsync(
            "query ($skip: Boolean!) { connection { nodes @skip(if: $skip) { name } edges } }");

        // act
        var instance = ExecutionGroupInstance.Open(plan, default);
        plan.TryGetNode(plan.Slots[1].Selection.Id, out var edges);
        var wait = instance.WaitForTurnAsync(edges!, TestContext.Current.CancellationToken);

        // assert
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Abandon_Should_CompleteAllWaiters_When_InstanceIsAbandoned()
    {
        // arrange
        var (instance, nodes) = await OpenAsync(Query);
        var token = TestContext.Current.CancellationToken;
        var second = instance.WaitForTurnAsync(nodes[1], token).AsTask();
        var third = instance.WaitForTurnAsync(nodes[2], token).AsTask();

        // act
        instance.Abandon();
        await Task.WhenAll(second, third).WaitAsync(TimeSpan.FromSeconds(15), token);

        // assert
        Assert.True(second.IsCompletedSuccessfully && third.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitForTurnAsync_Should_Cancel_When_TokenIsCanceled()
    {
        // arrange
        var (instance, nodes) = await OpenAsync(Query);
        using var cts = new CancellationTokenSource();
        var second = instance.WaitForTurnAsync(nodes[1], cts.Token).AsTask();

        // act
        await cts.CancelAsync();
        var exception = await Record.ExceptionAsync(() => second);

        // assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    private static string Describe(Task second, Task third)
        => $"second={second.IsCompletedSuccessfully} third={third.IsCompletedSuccessfully}";

    // The nodes are the three slots in sequence order followed by the node of the pageInfo selection.
    private static async Task<(ExecutionGroupInstance Instance, ExecutionGroupNode[] Nodes)> OpenAsync(string query)
    {
        var (_, rootSet, plan) = await ExecutionGroupTestOperation.CompileAsync(query);
        var instance = ExecutionGroupInstance.Open(plan, new ConditionFlags(ulong.MaxValue, null));
        var nodes = new List<ExecutionGroupNode>();

        foreach (var slot in plan.Slots)
        {
            Assert.True(plan.TryGetNode(slot.Selection.Id, out var node));
            nodes.Add(node);
        }

        var pageInfo = rootSet.Selections.ToArray().Single(s => s.Field.Name is "pageInfo");
        Assert.True(plan.TryGetNode(pageInfo.Id, out var pageInfoNode));
        nodes.Add(pageInfoNode);
        return (instance, [.. nodes]);
    }

    private static async Task<ExecutionGroupPlan> CreatePlanAsync(string query)
    {
        var (_, _, plan) = await ExecutionGroupTestOperation.CompileAsync(query);
        return plan;
    }
}
