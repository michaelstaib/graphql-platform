namespace HotChocolate.Execution;

/// <summary>
/// Releases the resources of a response whose execution can outlive the response stream.
/// </summary>
internal interface IRequestCleanup
{
    /// <summary>
    /// Registers a resource that is disposed once the response stream and the execution have released the response.
    /// </summary>
    /// <param name="disposable">
    /// The resource to dispose.
    /// </param>
    void Register(IDisposable disposable);

    /// <summary>
    /// Registers the resource that is disposed after every other registered resource.
    /// </summary>
    /// <param name="disposable">
    /// The resource to dispose.
    /// </param>
    void RegisterLast(IDisposable disposable);
}
