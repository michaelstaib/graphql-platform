using GreenDonut.Data;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Types.Pagination;

public class StreamPageConnectionExecutionGroupTests
{
    [Fact]
    public async Task Schema_Should_DefineTheExecutionGroup_When_FieldReturnsStreamPageConnection()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<StreamQueryType>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var schema = (Schema)executor.Schema;
        var entries = schema.QueryType.Fields["products"].ExecutionGroup!.Entries
            .Select(e => $"{e.Path} {e.Priority} streamable={e.IsStreamable} optional={e.IsOptional}");

        // assert
        entries.MatchInlineSnapshots(
        [
            "edges 0 streamable=True optional=True",
            "nodes 1 streamable=True optional=True",
            "pageInfo.hasNextPage 2147483647 streamable=False optional=True",
            "pageInfo.endCursor 2147483647 streamable=False optional=True",
            "pageInfo.forwardCursors 2147483647 streamable=False optional=True",
            "pageInfo.backwardCursors 2147483647 streamable=False optional=True"
        ]);
    }

    [Fact]
    public async Task Schema_Should_DefineTheExecutionGroup_When_ConnectionIsDerived()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<StreamQueryType>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var group = ((Schema)executor.Schema).QueryType.Fields["derived"].ExecutionGroup;

        // assert
        Assert.NotNull(group);
    }

    [Fact]
    public async Task Schema_Should_NotDefineAnExecutionGroup_When_FieldReturnsAClassicConnection()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<MixedQueryType>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var fields = ((Schema)executor.Schema).QueryType.Fields
            .Where(f => !f.IsIntrospectionField)
            .Select(f => $"{f.Name} group={f.ExecutionGroup is not null}")
            .OrderBy(t => t, StringComparer.Ordinal);

        // assert
        fields.MatchInlineSnapshots(
        [
            "classic group=False",
            "paged group=False",
            "streamed group=True"
        ]);
    }

    [Fact]
    public async Task Execute_Should_ReturnTheConnection_When_StreamPageConnectionIsReturnedByAMutation()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<StreamQueryType>()
            .AddMutationType<StreamMutationType>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync(
            "mutation { products { nodes { id } pageInfo { hasNextPage endCursor } } }",
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "products": {
                  "nodes": [],
                  "pageInfo": {
                    "hasNextPage": false,
                    "endCursor": null
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public async Task Schema_Should_Throw_When_DefaultResolverStrategyIsSerial()
    {
        // arrange
        var builder = new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<StreamQueryType>()
            .ModifyOptions(o => o.DefaultResolverStrategy = ExecutionStrategy.Serial);

        // act
        var exception = await Assert.ThrowsAsync<SchemaException>(
            async () => await builder.BuildRequestExecutorAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        // assert
        exception.Errors
            .Select(e => e.Message)
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal)
            .MatchInlineSnapshots(
            [
                "The execution group of field `StreamQuery.derived` is invalid: the entry `edges` references the field `DerivedProductConnection.edges` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.derived` is invalid: the entry `nodes` references the field `DerivedProductConnection.nodes` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.derived` is invalid: the entry `pageInfo.endCursor` references the field `PageInfo.endCursor` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.derived` is invalid: the entry `pageInfo.hasNextPage` references the field `PageInfo.hasNextPage` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.products` is invalid: the entry `edges` references the field `_0_ConnectionOfProduct.edges` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.products` is invalid: the entry `nodes` references the field `_0_ConnectionOfProduct.nodes` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.products` is invalid: the entry `pageInfo.endCursor` references the field `PageInfo.endCursor` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial.",
                "The execution group of field `StreamQuery.products` is invalid: the entry `pageInfo.hasNextPage` references the field `PageInfo.hasNextPage` that is not parallel executable."
                + " Execution group members are started by the group and cannot run serially, for example when the default resolver strategy is serial."
            ]);
    }

    public sealed class Product
    {
        public int Id { get; set; }
    }

    public sealed class StreamQuery
    {
        [UseConnection]
        public StreamPageConnection<Product> GetProducts()
            => new(StreamPage<Product>.Empty);

        [UseConnection]
        public DerivedProductConnection GetDerived()
            => new(StreamPage<Product>.Empty);
    }

    public sealed class StreamQueryType : ObjectType<StreamQuery>
    {
        protected override void Configure(IObjectTypeDescriptor<StreamQuery> descriptor)
        {
            descriptor.Field(t => t.GetProducts()).AddPagingArguments();
            descriptor.Field(t => t.GetDerived()).AddPagingArguments();
        }
    }

    public sealed class StreamMutation
    {
        [UseConnection]
        public StreamPageConnection<Product> Products()
            => new(StreamPage<Product>.Empty);
    }

    public sealed class StreamMutationType : ObjectType<StreamMutation>
    {
        protected override void Configure(IObjectTypeDescriptor<StreamMutation> descriptor)
            => descriptor.Field(t => t.Products()).AddPagingArguments();
    }

    public sealed class MixedQuery
    {
        [UsePaging]
        public IEnumerable<Product> GetClassic() => [];

        public PageConnection<Product> GetPaged()
            => new(Page<Product>.Create([], hasNextPage: false, hasPreviousPage: false, p => p.Id.ToString()));

        [UseConnection]
        public StreamPageConnection<Product> GetStreamed()
            => new(StreamPage<Product>.Empty);
    }

    public sealed class MixedQueryType : ObjectType<MixedQuery>
    {
        protected override void Configure(IObjectTypeDescriptor<MixedQuery> descriptor)
            => descriptor.Field(t => t.GetStreamed()).AddPagingArguments();
    }

    public sealed class DerivedProductConnection(StreamPage<Product> page) : StreamPageConnection<Product>(page);
}
