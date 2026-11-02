using HotChocolate.Execution;
using static HotChocolate.Fusion.Properties.FusionExecutionResources;

namespace HotChocolate.Fusion.Planning;

/// <summary>
/// Thrown when the fields of a <c>@defer</c>d fragment anchored on a mutation result
/// cannot be planned without running the mutation root field again.
/// </summary>
public sealed class DeferredMutationLookupRequiredException : Exception
{
    public DeferredMutationLookupRequiredException(SelectionPath path, string typeName)
        : base(FormatMessage(path, typeName))
    {
        Path = path;
        TypeName = typeName;
    }

    private static string FormatMessage(SelectionPath path, string typeName)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        return string.Format(
            DeferredMutationLookupRequiredException_NoLookupAvailable,
            path,
            typeName);
    }

    /// <summary>
    /// Gets the selection path of the unresolvable defer anchor.
    /// </summary>
    public SelectionPath Path { get; }

    /// <summary>
    /// Gets the name of the anchor type of the deferred fragment.
    /// </summary>
    public string TypeName { get; }
}
