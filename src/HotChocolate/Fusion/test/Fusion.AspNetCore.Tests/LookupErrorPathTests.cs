using HotChocolate.Execution.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Transport;
using HotChocolate.Transport.Http;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion;

public class LookupErrorPathTests : FusionTestBase
{
    // The selections of the entity, with the raising field first, in the middle and last, nullable, non-null and
    // unselected, and with a second source that nulls the entity first in one hop order.
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

    // The selections of an entity whose lookup reports an error for the whole entity and errors at soft and boom.
    private static readonly string[] s_rootAndFieldErrorSelections =
    [
        "soft nick label",
        "boom nick label",
        "nick soft code label",
        "soft nick xboom label",
        "nick soft code xboom label"
    ];

    // The selections of an entity whose lookup reports an error at a field that is not selected, with and without a
    // field that requires a field of the lookup.
    private static readonly string[] s_dependentSelections =
    [
        "nick total label",
        "total label",
        "nick label"
    ];

    // The selections of an entity whose lookup reports an error at a selected field that another field requires.
    private static readonly string[] s_fieldAndDependentSelections =
    [
        "soft total label",
        "total soft label"
    ];

    // The operations on an entity whose lookup reports an error below its value-type field, at the field or for the
    // whole entity, with the field that requires a sub-field of it excluded or selected, and with a second source that
    // nulls the entity.
    private static readonly string[] s_valueTypeOperations =
    [
        "query($s: Boolean! = false) { user(id: 7) { info { region } total @include(if: $s) } }",
        "{ user(id: 7) { info { region } total } }",
        "query($s: Boolean! = false) { user(id: 8) { info { region } total @include(if: $s) } }",
        "{ user(id: 8) { info { region } total } }",
        "query($s: Boolean! = false) { user(id: 9) { info { region } total @include(if: $s) } }",
        "query($s: Boolean! = false) { user(id: 7) { info { region } xboom total @include(if: $s) } }",
        "query($s: Boolean! = false) { user(id: 8) { info { region } xboom total @include(if: $s) } }"
    ];

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAnEntityReturnsNull()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(gateway, sequence, s_selections, "{ user(id: 2) { $ } }");

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
        var snapshot = await ExecuteInEveryHopOrderAsync(gateway, sequence, s_selections, "{ users { $ } }");

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
        var snapshot = await ExecuteInEveryHopOrderAsync(
            gateway,
            sequence,
            s_selections,
            "mutation { createUser(id: 2) { $ } }");

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
            s_selections,
            "{ user(id: 2) { id ... @defer { $ } } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportEachLookupErrorOnce_When_TheLookupFailsForTheEntityAndItsFields()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(
            gateway,
            sequence,
            s_rootAndFieldErrorSelections,
            "{ user(id: 4) { $ } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheDependentOnly_When_TheLookupFailsAtAnUnselectedField()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(
            gateway,
            sequence,
            s_dependentSelections,
            "{ user(id: 5) { $ } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorAtTheFieldAndOnTheDependent_When_TheLookupFailsAtASelectedField()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(sequence);

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(
            gateway,
            sequence,
            s_fieldAndDependentSelections,
            "{ user(id: 2) { $ } }");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnTheDependentOrOnceWithoutPath_When_TheLookupFailsBelowAValueType()
    {
        // arrange
        var sequence = new HopSequence();
        using var gateway = await CreateGatewayAsync(
            sequence,
            b => b.AddQueryType<ValueTypeSourceA.Query>(),
            b => b.AddQueryType<ValueTypeSourceB.Query>(),
            b => b.AddQueryType<ValueTypeSourceX.Query>(),
            b => b.AddQueryType<ValueTypeSourceC.Query>());

        // act
        var snapshot = await ExecuteInEveryHopOrderAsync(gateway, sequence, s_valueTypeOperations, "$");

        // assert
        snapshot.MatchMarkdownSnapshot();
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnceWithoutPath_When_TheLookupSelectingOnlyTheTypeNameRaises()
    {
        // arrange
        using var gateway = await CreateTypeNameGatewayAsync(b => b.AddQueryType<TypeNameReviewErrorSourceB.Query>());

        // act
        var result = await ExecuteAsync(
            gateway,
            "{ entity(id: 2) { id ... @defer { __typename ... on Product { far } } } }");

        // assert
        result.MatchInlineSnapshot(
            """
            {"data":{"entity":{"id":"2"}},"pending":[{"id":"0","path":["entity"]}],"hasNext":true}
            {"incremental":[{"id":"0","errors":[{"message":"review read failed"}],"data":{"__typename":"Review"}}],"completed":[{"id":"0"}],"hasNext":false}

            """);
    }

    [Fact]
    public async Task Execute_Should_ReportTheErrorOnlyOnFieldsWithoutAValue_When_TheLookupSelectingTheTypeNameRaises()
    {
        // arrange
        using var gateway = await CreateTypeNameGatewayAsync(b => b.AddQueryType<TypeNameSourceB.ErrorQuery>());

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
        using var gateway = await CreateTypeNameGatewayAsync(b => b.AddQueryType<TypeNameSourceB.NullQuery>());

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
        string[] selections,
        string template)
    {
        var snapshot = Snapshot.Create();

        foreach (var selection in selections)
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

    private Task<Gateway> CreateGatewayAsync(HopSequence sequence)
        => CreateGatewayAsync(
            sequence,
            b => b.AddQueryType<SourceA.Query>().AddMutationType<SourceA.Mutation>(),
            b => b.AddQueryType<SourceB.Query>(),
            b => b.AddQueryType<SourceX.Query>(),
            b => b.AddQueryType<SourceC.Query>());

    private async Task<Gateway> CreateGatewayAsync(
        HopSequence sequence,
        Action<IRequestExecutorBuilder> configureA,
        Action<IRequestExecutorBuilder> configureB,
        Action<IRequestExecutorBuilder> configureX,
        Action<IRequestExecutorBuilder> configureC)
    {
        var hopB = new SequencedHop(sequence, "B", deliveredOnResponse: false);
        var hopX = new SequencedHop(sequence, "X", deliveredOnResponse: false);
        var listener = new HopMergeListener(sequence, new() { ["b"] = "B", ["x"] = "X" });
        var a = CreateSourceSchema("a", configureA);
        var b = CreateSourceSchema("b", configureB, mockHttpResponse: hopB.SendAsync);
        var x = CreateSourceSchema("x", configureX, mockHttpResponse: hopX.SendAsync);
        var c = CreateSourceSchema("c", configureC);
        hopB.Connect(b);
        hopX.Connect(x);

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b), ("x", x), ("c", c)],
            configureGatewayBuilder: builder => builder.AddDiagnosticEventListener(_ => listener),
            includeOperationPlan: false);
    }

    private async Task<Gateway> CreateTypeNameGatewayAsync(
        Func<IRequestExecutorBuilder, IRequestExecutorBuilder> configureQuery)
    {
        var a = CreateSourceSchema(
            "a",
            b => b.AddQueryType<TypeNameSourceA.Query>()
                .AddType<TypeNameSourceA.Product>()
                .AddType<TypeNameSourceA.Review>());
        var b = CreateSourceSchema(
            "b",
            b => configureQuery(b).AddType<TypeNameSourceB.Review>());

        return await CreateCompositeSchemaAsync(
            [("a", a), ("b", b)],
            includeOperationPlan: false);
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

                if (id == 4)
                {
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("entity failed for 4")
                            .SetPath(context.Path)
                            .Build());
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("soft failed for 4")
                            .SetPath(context.Path.Append("soft"))
                            .Build());
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("boom failed for 4")
                            .SetPath(context.Path.Append("boom"))
                            .Build());
                    return null;
                }

                if (id == 5)
                {
                    context.ReportError(
                        ErrorBuilder.New()
                            .SetMessage("hidden failed for 5")
                            .SetPath(context.Path.Append("hidden"))
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

            public string? Sku => "sku-" + Id;
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

            public string Xboom => Id is 2 or 4 ? throw new GraphQLException("xboom failed for " + Id) : "xboom-" + Id;
        }
    }

    public static class SourceC
    {
        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id);
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? GetTotal([Require("sku")] string? sku) => "total-" + sku;
        }
    }

    public static class ValueTypeSourceA
    {
        [EntityKey("id")]
        public record User(int Id);

        public class Query
        {
            public User? GetUser(int id) => new(id);

            [Lookup, Internal]
            public User? GetUserById(int id) => new(id);
        }
    }

    public static class ValueTypeSourceB
    {
        public record Info(string? Sku);

        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id, IResolverContext context)
            {
                var path = id switch
                {
                    7 => context.Path.Append("info").Append("sku"),
                    8 => context.Path.Append("info"),
                    _ => context.Path
                };
                var message = id switch
                {
                    7 => "sku failed for 7",
                    8 => "info failed for 8",
                    _ => "entity failed for " + id
                };

                context.ReportError(ErrorBuilder.New().SetMessage(message).SetPath(path).Build());
                return null;
            }
        }

        [EntityKey("id")]
        public record User(int Id, [property: Shareable] Info Info);
    }

    public static class ValueTypeSourceX
    {
        public record Info(string? Region);

        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id, new Info("eu"));
        }

        [EntityKey("id")]
        public record User(int Id, [property: Shareable] Info Info)
        {
            public string Xboom => throw new GraphQLException("xboom failed for " + Id);
        }
    }

    public static class ValueTypeSourceC
    {
        public class Query
        {
            [Lookup, Internal]
            public User? GetUserById(int id) => new(id);
        }

        [EntityKey("id")]
        public record User(int Id)
        {
            public string? GetTotal([Require("info.sku")] string? sku) => "total-" + sku;
        }
    }

    public static class TypeNameSourceA
    {
        public class Query
        {
            public IEntity[] GetEntities() => [new Product(1), new Review(2)];

            public IEntity? GetEntity(int id) => id == 1 ? new Product(id) : new Review(id);

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

    public static class TypeNameReviewErrorSourceB
    {
        public class Query
        {
            [Lookup, Internal]
            public TypeNameSourceB.Product? GetProductById([ID] int id) => new(id);

            [Lookup, Internal]
            public TypeNameSourceB.Review? GetReviewById([ID] int id)
                => throw new GraphQLException("review read failed");
        }
    }
}
