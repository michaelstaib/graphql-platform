using System.Diagnostics;
using System.Text.Json.Nodes;
using HotChocolate.Transport.Http;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;
using OperationRequest = HotChocolate.Transport.OperationRequest;

namespace HotChocolate.Fusion;

[Collection(DeferredErrorPlacementGrowthCollection.Name)]
public class DeferredErrorPlacementGrowthTests : FusionTestBase
{
    // The largest ratio of the processor time for 4n items to the processor time for n items that counts as linear
    // growth.
    private const double MaxLinearGrowth = 8.0;

    [Fact]
    public async Task Defer_Should_GrowLinearly_When_DeferredFieldFailsForEveryItemOfALargeList()
    {
        // arrange
        var sourceA = CreateSourceSchema("A", x => x.AddQueryType<SourceA.Query>());
        var sourceB = CreateSourceSchema("B", x => x.AddQueryType<SourceB.Query>());
        using var gateway = await CreateCompositeSchemaAsync([("A", sourceA), ("B", sourceB)], includeOperationPlan: false);
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        const int n = 2000;

        // act
        var (growth, errorCount) = await MeasureGrowthAsync(client, n);

        // assert
        Assert.InRange(growth, 0, MaxLinearGrowth);
        Assert.Equal(n, errorCount);
    }

    // Gets the ratio of the least processor time of seven runs with 4n items to the least processor time of seven runs
    // with n items, and the number of errors with a path of the warm-up run with n items.
    private static async Task<(double Growth, int ErrorCount)> MeasureGrowthAsync(GraphQLHttpClient client, int n)
    {
        var queryOfN = CreateQuery(n);
        var queryOf4N = CreateQuery(n * 4);
        var errorCount = await ExecuteAsync(client, queryOfN);
        var timeOfN = double.MaxValue;
        var timeOf4N = double.MaxValue;

        for (var i = 0; i < 7; i++)
        {
            timeOfN = Math.Min(timeOfN, await MeasureAsync(queryOfN));
            timeOf4N = Math.Min(timeOf4N, await MeasureAsync(queryOf4N));
        }

        return (timeOf4N / timeOfN, errorCount);

        async Task<double> MeasureAsync(string query)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var start = GetProcessorTime();
            await ExecuteAsync(client, query);
            return (GetProcessorTime() - start).TotalMilliseconds;
        }

        static TimeSpan GetProcessorTime()
        {
            using var process = Process.GetCurrentProcess();
            return process.TotalProcessorTime;
        }

        static string CreateQuery(int count)
            => $"query {{ userList(count: {count}) {{ id ... @defer {{ nick }} }} }}";
    }

    private static async Task<int> ExecuteAsync(GraphQLHttpClient client, string query)
    {
        using var result = await client.PostAsync(
            new OperationRequest(query),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        var body = await result.HttpResponseMessage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var errorCount = 0;

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = JsonNode.Parse(line)!.AsObject();

            foreach (var entry in payload["incremental"]?.AsArray() ?? [])
            {
                errorCount += entry!["errors"]?.AsArray().Count(e => e!["path"] is not null) ?? 0;
            }
        }

        return errorCount;
    }

    private static class SourceA
    {
        public class Query
        {
            public IEnumerable<User> GetUserList(int count) => Enumerable.Range(1, count).Select(t => new User(t));
        }

        public record User([property: Shareable] int Id);
    }

    private static class SourceB
    {
        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id);
        }

        public record User(int Id)
        {
            public string? GetNick() => throw new GraphQLException("Nick failed.");
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DeferredErrorPlacementGrowthCollection
{
    public const string Name = "DeferredErrorPlacementGrowth";
}
