using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning.Partitioners;

internal readonly ref struct SelectionSetPartitionerInput
{
    public required string SchemaName { get; init; }
    public required SelectionSet SelectionSet { get; init; }
    public required ISelectionSetIndex SelectionSetIndex { get; init; }
    public SelectionSetNode? ProvidedSelectionSet { get; init; }
    public bool PruneUnprovidedAbstractBranches { get; init; }
    public bool TreatSourceExternalAsUnresolvable { get; init; }

    /// <summary>
    /// When partitioning a lookup entry root, limits <c>sourceExternal</c> fields to those
    /// included in the entering lookup key. Set to <c>null</c> to disable this constraint.
    /// </summary>
    public SelectionSetNode? EntryKeyCoverage { get; init; }

    /// <summary>
    /// Maps the <c>@defer</c> directive of a fragment to the id of its delivery group. Every selection of
    /// such a fragment is left unresolved. <c>null</c> plans a <c>@defer</c> fragment like any other fragment.
    /// </summary>
    public IReadOnlyDictionary<DirectiveNode, int>? DeferredFragments { get; init; }
}
