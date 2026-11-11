using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

[Collection(KeyHopFailureGrowthCollection.Name)]
public class KeyHopFailureGrowthTests : FusionTestBase
{
    // The largest ratio of the time for 4n items to the time for n items that counts as linear growth.
    private const double MaxLinearGrowth = 8.0;

    [Fact]
    public async Task Execute_Should_GrowLinearly_When_EverySourceFailsForTheValueTypeOfManyItems()
    {
        // arrange
        var a = CreateSourceSchema("a", b => b.AddQueryType<KeyHopFailureTests.A.Query>());
        var code = CreateSourceSchema("code", b => b.AddQueryType<PInfoCodeNull.Query>());
        var label = CreateSourceSchema("label", b => b.AddQueryType<PInfoLabelNull.Query>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("code", code), ("label", label)],
            includeOperationPlan: false);

        // act
        var (growth, result) = await MeasureGrowthAsync(gateway, "info { code label }", 4000);

        // assert
        Assert.InRange(growth, 0, MaxLinearGrowth);
        SummarizeErrors(result).MatchInlineSnapshot(
            """
            first item: {"info":{"code":null,"label":null}}
            8000 x The batch size exceeds the maximum allowed batch size of 1024. at no path
            4000 x Unexpected Execution Error at userList/i/info/code
            4000 x Unexpected Execution Error at userList/i/info/label

            """);
    }

    [Fact]
    public async Task Execute_Should_GrowLinearly_When_TheRequirementSourceFailsForManyDependents()
    {
        // arrange
        var a = CreateSourceSchema("a", builder => builder.AddQueryType<KeyHopFailureTests.A.Query>());
        var b = CreateSourceSchema("b", builder => builder.AddQueryType<KeyHopFailureTests.B.Query>());
        var c = CreateSourceSchema("c", builder => builder.AddQueryType<KeyHopFailureTests.CNested.Query>());
        using var gateway = await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("c", c)],
            includeOperationPlan: false);

        // act
        var (growth, result) = await MeasureGrowthAsync(gateway, "total", 4000);

        // assert
        Assert.InRange(growth, 0, MaxLinearGrowth);
        SummarizeErrors(result).MatchInlineSnapshot(
            """
            first item: {"total":null}
            4000 x The batch size exceeds the maximum allowed batch size of 1024. at no path
            4000 x Unexpected Execution Error at userList/i/total

            """);
    }

    // Gets the ratio of the fastest of seven runs of userList with 4n items to the fastest of seven runs with n items,
    // and the response of the warm-up run with n items.
    private static async Task<(double Growth, string Result)> MeasureGrowthAsync(
        Gateway gateway,
        string selection,
        int n)
    {
        var queryOfN = CreateUserListQuery(selection, n);
        var queryOf4N = CreateUserListQuery(selection, n * 4);
        var result = await ExecuteAsync(gateway, queryOfN);
        var timeOfN = double.MaxValue;
        var timeOf4N = double.MaxValue;

        for (var i = 0; i < 7; i++)
        {
            timeOfN = Math.Min(timeOfN, await MeasureAsync(queryOfN));
            timeOf4N = Math.Min(timeOf4N, await MeasureAsync(queryOf4N));
        }

        return (timeOf4N / timeOfN, result);

        async Task<double> MeasureAsync(string query)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            await ExecuteAsync(gateway, query);
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        static string CreateUserListQuery(string selection, int count)
            => $"{{ userList(ids: [{string.Join(",", Enumerable.Range(1, count))}]) {{ {selection} }} }}";
    }

    private static async Task<string> ExecuteAsync(Gateway gateway, string query)
    {
        using var client = gateway.CreateClient();
        using var content = new StringContent(
            JsonSerializer.Serialize(new { query }),
            Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/graphql")
        {
            Content = content
        };
        request.Headers.Add("Accept", "application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    // Gets the first item and the number of errors per message and path, with list indexes replaced by i.
    private static string SummarizeErrors(string result)
    {
        var response = JsonNode.Parse(result)!;
        var data = response["data"]!;
        var errors = response["errors"] as JsonArray ?? [];
        var text = new StringBuilder();
        text.AppendLine($"first item: {data["userList"]?[0]?.ToJsonString()}");

        var groups = errors
            .Select(e => $"{e!["message"]} at {GetPathShape(e["path"])}")
            .GroupBy(e => e)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            text.AppendLine($"{group.Count()} x {group.Key}");
        }

        return text.ToString();

        static string GetPathShape(JsonNode? path)
            => path is JsonArray segments
                ? string.Join("/", segments.Select(s => s!.GetValueKind() is JsonValueKind.Number ? "i" : s.ToString()))
                : "no path";
    }

    public static class PInfoCodeNull
    {
        public record Info(string? Code);

        public record User(int Id, [property: Shareable] Info? Info);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, null);
        }
    }

    public static class PInfoLabelNull
    {
        public record Info(string? Label);

        public record User(int Id, [property: Shareable] Info? Info);

        public class Query
        {
            [Internal]
            public Lookups Lookups { get; } = new();
        }

        [Internal]
        public class Lookups
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, null);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KeyHopFailureGrowthCollection
{
    public const string Name = "KeyHopFailureGrowth";
}
