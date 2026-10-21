using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Execution.ExecutionGroups;

internal static class ExecutionGroupTestSchema
{
    public static ValueTask<IRequestExecutor> CreateAsync(ExecutionScript script)
        => new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(descriptor =>
            {
                descriptor.Name("Query");

                descriptor
                    .Field("connection")
                    .Type("Connection")
                    .Resolve(_ => new Connection())
                    .ExecutionGroup(
                        new ExecutionGroupEntry("nodes", 0, isStreamable: true),
                        new ExecutionGroupEntry("edges", 1, isStreamable: false),
                        new ExecutionGroupEntry("pageInfo.hasNextPage", 2, isStreamable: false));

                descriptor.Field("holder").Type("Holder").Resolve(_ => new Holder());

                descriptor
                    .Field("reversed")
                    .Type("Connection")
                    .Resolve(_ => new Connection())
                    .ExecutionGroup(
                        new ExecutionGroupEntry("pageInfo.hasNextPage", 0, isStreamable: false),
                        new ExecutionGroupEntry("nodes", 1, isStreamable: true),
                        new ExecutionGroupEntry("edges", 2, isStreamable: false));

                descriptor
                    .Field("strict")
                    .Type("StrictConnection")
                    .Resolve(_ => new StrictConnection())
                    .ExecutionGroup(
                        new ExecutionGroupEntry("pageInfo.hasNextPage", 0, isStreamable: false),
                        new ExecutionGroupEntry("nodes", 1, isStreamable: true));

                descriptor
                    .Field("pages")
                    .Type("[Page!]!")
                    .Resolve(_ => new object[] { new PageA(), new PageB() })
                    .ExecutionGroup(
                        new ExecutionGroupEntry("pageInfo.hasNextPage", 0, isStreamable: false),
                        new ExecutionGroupEntry("nodes", 1, isStreamable: true));
            })
            .AddInterfaceType(descriptor =>
            {
                descriptor.Name("Page");
                descriptor.Field("nodes").Type("[Item!]!");
                descriptor.Field("pageInfo").Type("PageInfo");
            })
            .AddObjectType<PageA>(descriptor =>
            {
                descriptor.Name("PageA").Implements("Page");
                descriptor.Field("nodes").Type("[Item!]!").Resolve(ctx => script.RunAsync(ctx, Items()));
                descriptor
                    .Field("pageInfo")
                    .Type("PageInfo")
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo(script)));
            })
            .AddObjectType<PageB>(descriptor =>
            {
                descriptor.Name("PageB").Implements("Page");
                descriptor.Field("nodes").Type("[Item!]!").Resolve(ctx => script.RunAsync(ctx, Items()));
                descriptor
                    .Field("pageInfo")
                    .Type("PageInfo")
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo(script)));
            })
            .AddObjectType<Connection>(descriptor =>
            {
                descriptor.Name("Connection");
                descriptor.Field("nodes").Type("[Item!]!").Resolve(ctx => script.RunAsync(ctx, Items()));
                descriptor.Field("edges").Type("[String!]!").Resolve(ctx => script.RunAsync(ctx, new[] { "e1" }));
                descriptor
                    .Field("pageInfo")
                    .Type("PageInfo")
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo(script)));
                descriptor.Field("probe").Type("String").Resolve(ctx => script.Resolve(ctx, "probe"));
            })
            .AddObjectType<Holder>(descriptor =>
            {
                descriptor.Name("Holder");
                descriptor
                    .Field("batched")
                    .Type("Connection")
                    .ResolveBatch(
                        contexts => new ValueTask<IReadOnlyList<ResolverResult>>(
                            contexts.Select(_ => ResolverResult.Ok(new Connection())).ToArray()))
                    .ExecutionGroup(
                        new ExecutionGroupEntry("nodes", 0, isStreamable: true),
                        new ExecutionGroupEntry("edges", 1, isStreamable: false));

                descriptor
                    .Field("wrapper")
                    .Type("Wrapper")
                    .ResolveBatch(
                        contexts => new ValueTask<IReadOnlyList<ResolverResult>>(
                            contexts.Select(_ => ResolverResult.Ok(new Wrapper(new Connection()))).ToArray()));
            })
            .AddObjectType<Wrapper>(descriptor =>
            {
                descriptor.Name("Wrapper");
                descriptor
                    .Field(w => w.Connection)
                    .Type("Connection")
                    .ExecutionGroup(
                        new ExecutionGroupEntry("nodes", 0, isStreamable: true),
                        new ExecutionGroupEntry("edges", 1, isStreamable: false));
            })
            .AddObjectType<StrictConnection>(descriptor =>
            {
                descriptor.Name("StrictConnection");
                descriptor.Field("nodes").Type("[Item!]!").Resolve(ctx => script.RunAsync(ctx, Items()));
                descriptor
                    .Field("pageInfo")
                    .Type("PageInfo!")
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo(script)));
            })
            .AddObjectType<PageInfo>(descriptor =>
            {
                descriptor.Name("PageInfo");
                descriptor.Field("hasNextPage").Type("Boolean!").Resolve(ctx => script.RunAsync(ctx, true));
                descriptor.Field("probe").Type("String").Resolve(ctx => script.Resolve(ctx, "probe"));
                descriptor.Field(p => p.Bad).Type<NonNullType<StringType>>();
            })
            .AddObjectType<Item>(descriptor =>
            {
                descriptor.Name("Item");
                descriptor.Field("name").Type("String!").Resolve(ctx => ctx.Parent<Item>().Name);
                descriptor
                    .Field("children")
                    .Type("Connection")
                    .Resolve(_ => new Connection())
                    .ExecutionGroup(
                        new ExecutionGroupEntry("nodes", 0, isStreamable: true),
                        new ExecutionGroupEntry("edges", 1, isStreamable: false));
            })
            .ModifyOptions(options =>
            {
                options.EnableDefer = true;
                options.EnableStream = true;
            })
            .BuildRequestExecutorAsync();

    private static Item[] Items() => [new Item("a"), new Item("b")];

    internal sealed class Connection;

    internal sealed class Holder;

    internal sealed record Wrapper(Connection Connection);

    internal sealed class StrictConnection;

    internal sealed class PageA;

    internal sealed class PageB;

    internal sealed class PageInfo(ExecutionScript script)
    {
        public string? Bad
        {
            get
            {
                script.Reached("bad");
                return null;
            }
        }
    }

    internal sealed record Item(string Name);
}
