namespace HotChocolate.Types.Pagination;

[Flags]
internal enum StreamPageReads
{
    None = 0,
    TotalCount = 1,
    FirstRow = 2,
    AllRows = 4
}
