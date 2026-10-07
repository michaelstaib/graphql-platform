using System.Collections.Immutable;
using System.Security.Claims;
using HotChocolate.Fusion.Execution;

namespace HotChocolate.Fusion.Authorization.Audit;

/// <summary>
/// A scope that keeps its entries until it is committed and then hands them over once.
/// </summary>
public abstract class AuditScope : IAuditScope
{
    private readonly ImmutableArray<AuditLogEntry>.Builder _entries =
        ImmutableArray.CreateBuilder<AuditLogEntry>();
    private bool _isCommitted;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditScope"/>.
    /// </summary>
    /// <param name="info">
    /// The identity of the variable set.
    /// </param>
    /// <param name="user">
    /// The principal of the request, which the scope captures as values.
    /// </param>
    /// <param name="context">
    /// The provider data of the scope.
    /// </param>
    protected AuditScope(
        AuditScopeInfo info,
        ClaimsPrincipal user,
        ImmutableDictionary<string, string> context)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(context);

        Info = info;
        Subject = AuditSubject.Capture(user);
        Context = context;
    }

    /// <inheritdoc />
    public AuditScopeInfo Info { get; }

    /// <inheritdoc />
    public AuditSubject Subject { get; }

    /// <inheritdoc />
    public ImmutableDictionary<string, string> Context { get; }

    /// <inheritdoc />
    public bool IsRecording => true;

    /// <inheritdoc />
    public void Record(in AuditLogEntry entry)
    {
        if (_isCommitted)
        {
            throw ThrowHelper.AuditScopeAlreadyCommitted();
        }

        _entries.Add(entry);
    }

    /// <inheritdoc />
    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (_isCommitted)
        {
            throw ThrowHelper.AuditScopeAlreadyCommitted();
        }

        _isCommitted = true;

        return OnCommitAsync(_entries.ToImmutable(), cancellationToken);
    }

    /// <summary>
    /// Makes the entries of the scope durable.
    /// </summary>
    /// <param name="entries">
    /// The entries in the order they were recorded.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that signals that the request was aborted.
    /// </param>
    protected abstract ValueTask OnCommitAsync(
        ImmutableArray<AuditLogEntry> entries,
        CancellationToken cancellationToken);
}
