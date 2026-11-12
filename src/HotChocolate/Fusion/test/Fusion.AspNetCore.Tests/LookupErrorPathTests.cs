using HotChocolate.Fusion.Diagnostics;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Resolvers;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public class LookupErrorPathTests : FusionTestBase
{
    // The selections of the entity, with the raising field first, in the middle and last. boom is non-null and raises,
    // so the lookup of B returns null. For soft, which is nullable, and for ghost, the lookup of B reports an error at
    // soft or at a field that is not selected and returns null. xboom is non-null and raises in X, so the entity is
    // null before the result of B arrives when X is answered first.
    private static readonly string[] s_selections =
    [
        "boom nick title label",
        "nick boom title label",
        "nick title boom label",
        "soft nick title label",
        "nick soft title label",
        "nick title soft label",
        "nick soft code label",
        "nick ghost label",
        "nick boom xboom label",
        "nick soft xboom label",
        "nick ghost xboom label"
    ];

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAnEntityReturnsNull()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(gateway, sequence, "{ user(id: 2) { $ } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAListItemReturnsNull()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(gateway, sequence, "{ users { $ } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAMutationResultReturnsNull()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(gateway, sequence, "mutation { createUser(id: 2) { $ } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheRaisingField_When_TheDeferredLookupOfAnEntityReturnsNull()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(
            gateway,
            sequence,
            "{ user(id: 2) { id ... @defer { $ } } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnFieldsWithoutAValue_When_TheLookupSelectingTheTypeNameRaises()
    {
        // arrange
        using var gateway = await CreateTypeNameGatewayAsync(raisesError: true);

        // act
        var plain = await ExecuteAsync(gateway, "{ entities { id __typename ... on Product { far } } }");
        var deferred = await ExecuteAsync(
            gateway,
            "{ entities { id ... @defer { __typename ... on Product { far } } } }");

        // assert
        Snapshot.Create()
            .Add(plain, "plain", "text")
            .Add(deferred, "deferred", "text")
            .MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_CompleteOnlyFieldsWithoutAValue_When_TheLookupSelectingTheTypeNameReturnsNull()
    {
        // arrange
        using var gateway = await CreateTypeNameGatewayAsync(raisesError: false);

        // act
        var plain = await ExecuteAsync(gateway, "{ entities { id __typename ... on Product { far } } }");
        var deferred = await ExecuteAsync(
            gateway,
            "{ entities { id ... @defer { __typename ... on Product { far } } } }");

        // assert
        Snapshot.Create()
            .Add(plain, "plain", "text")
            .Add(deferred, "deferred", "text")
            .MatchMarkdownSnapshot();
    }

    private static async Task<Snapshot> ExecuteInEveryHopOrderAsync(
        Gateway gateway,
        HopSequence sequence,
        string template)
    {
        var snapshot = Snapshot.Create();

        foreach (var selection in s_selections)
        {
            var query = template.Replace("$", selection);
            sequence.Start(["B", "X"]);
            var bFirst = await ExecuteAsync(gateway, query);
            sequence.Start(["X", "B"]);
            var xFirst = await ExecuteAsync(gateway, query);

            Assert.Equal(bFirst, xFirst);
            snapshot.Add(bFirst, query, "text");
        }

        sequence.Start([]);
        return snapshot;
    }

    private static async Task<string> ExecuteAsync(Gateway gateway, string query)
    {
        using var client = GraphQLHttpClient.Create(gateway.CreateClient());
        using var response = await client.PostAsync(
            new OperationRequest(query),
            new Uri("http://localhost:5000/graphql"),
            TestContext.Current.CancellationToken);
        var body = await response.HttpResponseMessage.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        return body.ReplaceLineEndings("\n");
    }

    private async Task<Gateway> CreateGatewayAsync(HopSequence sequence)
    {
        var hopB = new SequencedHop(sequence, "B");
        var hopX = new SequencedHop(sequence, "X");
        var listener = new HopMergeListener(sequence, new() { ["b"] = "B", ["x"] = "X" });
        var a = CreateSourceSchema(
            "a",
            b => b.AddQueryType<SourceA.Query>().AddMutationType<SourceA.Mutation>());
        var b = CreateSourceSchema("b", b => b.AddQueryType<SourceB.Query>(), mockHttpResponse: hopB.SendAsync);
        var x = CreateSourceSchema("x", b => b.AddQueryType<SourceX.Query>(), mockHttpResponse: hopX.SendAsync);
        hopB.Connect(b);
        hopX.Connect(x);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("x", x)],
            configureGatewayBuilder: builder => builder.AddDiagnosticEventListener(_ => listener),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateTypeNameGatewayAsync(bool raisesError)
    {
        var a = CreateSourceSchema(
            "a",
            b => b.AddQueryType<TypeNameSourceA.Query>()
                .AddType<TypeNameSourceA.Product>()
                .AddType<TypeNameSourceA.Review>());
        var b = CreateSourceSchema(
            "b",
            b => (raisesError
                    ? b.AddQueryType<TypeNameSourceB.ErrorQuery>()
                    : b.AddQueryType<TypeNameSourceB.NullQuery>())
                .AddType<TypeNameSourceB.Review>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b)],
            includeOperationPlan: false);
    }

    // Starts runs that answer the hops in a given order.
    private sealed class HopSequence
    {
        private HopRun _run = new([]);

        public HopRun Current => _run;

        public void Start(string[] order) => _run = new HopRun(order);
    }

    // Answers each hop once the hop before it in the order was merged.
    private sealed class HopRun(string[] order)
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        private readonly TaskCompletionSource[] _merged = order
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();

        public async Task WaitForTurnAsync(string hop, CancellationToken cancellationToken)
        {
            var index = Array.IndexOf(order, hop);

            if (index > 0)
            {
                await _merged[index - 1].Task.WaitAsync(s_timeout, cancellationToken);
            }
        }

        public void MarkMerged(string hop)
        {
            var index = Array.IndexOf(order, hop);

            if (index >= 0)
            {
                _merged[index].TrySetResult();
            }
        }
    }

    // Sends the requests of a hop to its source schema once it is the turn of the hop.
    private sealed class SequencedHop(HopSequence sequence, string hop)
    {
        private HttpMessageInvoker? _invoker;

        public void Connect(TestServer server)
            => _invoker = new HttpMessageInvoker(server.CreateHandler());

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await sequence.Current.WaitForTurnAsync(hop, cancellationToken);
            var response = await _invoker!.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync();
            return response;
        }
    }

    // Marks the hop of a source schema merged once its node has queued its result for merging.
    private sealed class HopMergeListener(HopSequence sequence, Dictionary<string, string> hopsBySchema)
        : FusionExecutionDiagnosticEventListener
    {
        public override IDisposable ExecuteOperationNode(
            OperationPlanContext context,
            OperationExecutionNode node,
            string schemaName)
            => TryMarkMerged(schemaName) ?? base.ExecuteOperationNode(context, node, schemaName);

        public override IDisposable ExecuteOperationBatchNode(
            OperationPlanContext context,
            OperationBatchExecutionNode node,
            string schemaName)
            => TryMarkMerged(schemaName) ?? base.ExecuteOperationBatchNode(context, node, schemaName);

        private Merge? TryMarkMerged(string schemaName)
            => hopsBySchema.TryGetValue(schemaName, out var hop) ? new Merge(sequence.Current, hop) : null;

        private sealed class Merge(HopRun run, string hop) : IDisposable
        {
            public void Dispose() => run.MarkMerged(hop);
        }
    }

    public static class SourceA
    {
        [EntityKey("id")]
        public record User(int Id);

        public class Query
        {
            public User? GetUser(int id) => new(id);

            public List<User?> GetUsers() => [new(1), new(2), new(3)];

            [Lookup, Internal]
            public User? GetUserById(int id) => new(id);
        }

        public class Mutation
        {
            public User? CreateUser(int id) => new(id);
        }
    }

    public static class SourceB
    {
        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id, IResolverContext context)
            {
                if (id == 2 && context.IsSelected("soft"))
                {
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("soft failed for 2")
                            .SetPath(context.Path.Append("soft"))
                            .Build());
                    return null;
                }

                if (id == 2 && context.IsSelected("ghost"))
                {
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("hidden failed for 2")
                            .SetPath(context.Path.Append("hidden"))
                            .Build());
                    return null;
                }

                return new User(id);
            }
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? Nick => "nick-" + Id;

            public string? Ghost => "ghost-" + Id;

            public string? Title => "title-" + Id;

            public string Code => "code-" + Id;

            public string Boom => Id == 2 ? throw new GraphQLException("boom failed for 2") : "boom-" + Id;

            public string? Soft => "soft-" + Id;
        }
    }

    public static class SourceX
    {
        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id);
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? Label => "label-" + Id;

            public string Xboom => Id == 2 ? throw new GraphQLException("xboom failed for 2") : "xboom-" + Id;
        }
    }

    public static class TypeNameSourceA
    {
        public class Query
        {
            public IEntity[] GetEntities() => [new Product(1), new Review(2)];

            [Lookup, Internal]
            public Product? GetProductById([ID] int id) => new(id);

            [Lookup, Internal]
            public Review? GetReviewById([ID] int id) => new(id);
        }

        [InterfaceType("Entity")]
        public interface IEntity
        {
            [ID]
            int Id { get; }
        }

        public record Product([property: ID] int Id) : IEntity
        {
            public string Local => "local " + Id;
        }

        public record Review([property: ID] int Id) : IEntity
        {
            public int Rating => Id;
        }
    }

    public static class TypeNameSourceB
    {
        [GraphQLName("Query")]
        public class ErrorQuery
        {
            [Lookup, Internal]
            public Product? GetProductById([ID] int id) => throw new GraphQLException("source read failed");

            [Lookup, Internal]
            public Review? GetReviewById([ID] int id) => new(id);
        }

        [GraphQLName("Query")]
        public class NullQuery
        {
            [Lookup, Internal]
            public Product? GetProductById([ID] int id) => null;

            [Lookup, Internal]
            public Review? GetReviewById([ID] int id) => new(id);
        }

        public record Product([property: ID] int Id)
        {
            public string Far => "far " + Id;
        }

        public record Review([property: ID] int Id)
        {
            public string Other => "other " + Id;
        }
    }
}
