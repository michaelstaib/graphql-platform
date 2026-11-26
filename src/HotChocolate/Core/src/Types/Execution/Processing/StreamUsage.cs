namespace HotChocolate.Execution.Processing;

/// <summary>
/// Represents the active <c>@stream</c> directive of a field selection.
/// </summary>
/// <param name="InitialCount">
/// The number of list items that are delivered with the parent payload.
/// </param>
/// <param name="Label">
/// The label of the directive, or <c>null</c> if none was specified.
/// </param>
internal readonly record struct StreamUsage(int InitialCount, string? Label)
{
    /// <summary>
    /// Gets the active <c>@stream</c> directive of the selection, resolving variables with
    /// <paramref name="variables"/>. Returns <c>false</c> if the selection has no active directive.
    /// </summary>
    public static bool TryCreate(
        Selection selection,
        IVariableValueCollection variables,
        out StreamUsage usage)
    {
        if (selection.StreamArguments is { } arguments && arguments.IsEnabled(variables))
        {
            usage = new StreamUsage(arguments.GetInitialCount(variables), arguments.GetLabel(variables));
            return true;
        }

        usage = default;
        return false;
    }
}
