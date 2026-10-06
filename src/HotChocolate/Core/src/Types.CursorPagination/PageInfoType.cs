using HotChocolate.Resolvers;
using HotChocolate.Types.Composite;

namespace HotChocolate.Types.Pagination;

public class PageInfoType : ObjectType<ConnectionPageInfo>
{
    protected override void Configure(
        IObjectTypeDescriptor<ConnectionPageInfo> descriptor)
    {
        if (descriptor.Extend().Context.Options.ApplyShareableToPageInfo)
        {
            descriptor.Directive(Shareable.Instance);
        }

        descriptor
            .Name(Names.PageInfo)
            .Description("Information about pagination in a connection.")
            .BindFields(BindingBehavior.Explicit);

        descriptor
            .Field(t => t.HasNextPageAsync(default))
            .Type<NonNullType<BooleanType>>()
            .Name(Names.HasNextPage)
            .Description(
                "Indicates whether more edges exist following "
                + "the set defined by the clients arguments.")
            .Extend()
            .OnBeforeCreate(
                c => c.PureResolver = ctx => GetPageInfo(ctx).HasNextPageAsync(ctx.RequestAborted).Result);

        descriptor
            .Field(t => t.HasPreviousPageAsync(default))
            .Type<NonNullType<BooleanType>>()
            .Name(Names.HasPreviousPage)
            .Description(
                "Indicates whether more edges exist prior "
                + "the set defined by the clients arguments.")
            .Extend()
            .OnBeforeCreate(
                c => c.PureResolver = ctx => GetPageInfo(ctx).HasPreviousPageAsync(ctx.RequestAborted).Result);

        descriptor
            .Field(t => t.GetStartCursorAsync(default))
            .Type<StringType>()
            .Name(Names.StartCursor)
            .Description("When paginating backwards, the cursor to continue.")
            .Extend()
            .OnBeforeCreate(
                c => c.PureResolver = ctx => GetPageInfo(ctx).GetStartCursorAsync(ctx.RequestAborted).Result);

        descriptor
            .Field(t => t.GetEndCursorAsync(default))
            .Type<StringType>()
            .Name(Names.EndCursor)
            .Description("When paginating forwards, the cursor to continue.")
            .Extend()
            .OnBeforeCreate(
                c => c.PureResolver = ctx => GetPageInfo(ctx).GetEndCursorAsync(ctx.RequestAborted).Result);
    }

    private static ConnectionPageInfo GetPageInfo(IResolverContext context)
        => context.Parent<ConnectionPageInfo>();

    public static class Names
    {
        public const string PageInfo = "PageInfo";
        public const string HasNextPage = "hasNextPage";
        public const string HasPreviousPage = "hasPreviousPage";
        public const string StartCursor = "startCursor";
        public const string EndCursor = "endCursor";
    }
}
