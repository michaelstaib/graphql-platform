namespace HotChocolate.Fusion.Authorization.Audit;

/// <summary>
/// Creates the audit trail of an executor invocation.
/// </summary>
public interface IAuditProvider
{
    /// <summary>
    /// Creates the trail for one executor invocation, which is a single request, a request batch,
    /// or a variable batch. The method runs for every invocation, including those that do not
    /// touch a protected selection.
    /// </summary>
    /// <param name="requestServices">
    /// The request scoped service provider of the invocation.
    /// </param>
    IAuditTrail CreateTrail(IServiceProvider requestServices);
}
