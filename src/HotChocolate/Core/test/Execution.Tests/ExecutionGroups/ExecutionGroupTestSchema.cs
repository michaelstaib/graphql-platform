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
            })
            .AddObjectType<Connection>(descriptor =>
            {
                descriptor.Name("Connection");
                descriptor.Field("nodes").Type("[Item!]!").Resolve(ctx => script.RunAsync(ctx, Items()));
                descriptor.Field("edges").Type("[String!]!").Resolve(ctx => script.RunAsync(ctx, new[] { "e1" }));
                descriptor
                    .Field("pageInfo")
                    .Type("PageInfo")
                    .Resolve(ctx => script.Resolve(ctx, new PageInfo()));
                descriptor.Field("probe").Type("String").Resolve(ctx => script.Resolve(ctx, "probe"));
            })
            .AddObjectType<StrictConnection>(descriptor =>
            {
                descriptor.Name("StrictConnection");
                descriptor.Field("nodes").Type("[Item!]!").Resolve(ctx => script.RunAsync(ctx, Items()));
                descriptor.Field("pageInfo").Type("PageInfo!").Resolve(ctx => script.Resolve(ctx, new PageInfo()));
            })
            .AddObjectType<PageInfo>(descriptor =>
            {
                descriptor.Name("PageInfo");
                descriptor.Field("hasNextPage").Type("Boolean!").Resolve(ctx => script.RunAsync(ctx, true));
                descriptor.Field("probe").Type("String").Resolve(ctx => script.Resolve(ctx, "probe"));
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

    internal sealed class StrictConnection;

    internal sealed class PageInfo;

    internal sealed record Item(string Name);
}
