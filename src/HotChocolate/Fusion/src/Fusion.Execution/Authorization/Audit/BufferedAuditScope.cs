using System.Collections.Immutable;
using System.Security.Claims;

namespace HotChocolate.Fusion.Authorization.Audit;

/// <summary>
/// An audit scope that keeps its entries in memory and replays them into a scope of the trail.
/// </summary>
internal sealed class BufferedAuditScope : IAuditScope
{
    private readonly ClaimsPrincipal _user;
    private readonly List<AuditLogEntry> _entries = [];

    /// <summary>
    /// Initializes a new instance of <see cref="BufferedAuditScope"/>.
    /// </summary>
    /// <param name="trail">
    /// The trail that receives the replayed scope.
    /// </param>
    /// <param name="info">
    /// The identity of the variable set.
    /// </param>
    /// <param name="user">
    /// The principal of the evaluation.
    /// </param>
    public BufferedAuditScope(IAuditTrail trail, AuditScopeInfo info, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(user);

        Trail = trail;
        Info = info;
        _user = user;
    }

    /// <inheritdoc />
    public IAuditTrail Trail { get; }

    /// <inheritdoc />
    public AuditScopeInfo Info { get; }

    /// <inheritdoc />
    public AuditSubject Subject => AuditSubject.Capture(_user);

    /// <inheritdoc />
    public ImmutableDictionary<string, string> Context { get; } =
#if NET10_0_OR_GREATER
        [];
#else
        ImmutableDictionary<string, string>.Empty;
#endif

    /// <inheritdoc />
    public bool IsRecording => true;

    /// <summary>
    /// Gets the failure recorded with <see cref="Fail"/>, or <c>null</c> if the evaluation did not fail.
    /// </summary>
    public Exception? Failure { get; private set; }

    /// <inheritdoc />
    public void Record(in AuditLogEntry entry)
        => _entries.Add(entry);

    /// <inheritdoc />
    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Failure ??= exception;
    }

    /// <summary>
    /// Opens a scope on the trail, replays the recorded entries and the failure into it and
    /// returns it uncommitted.
    /// </summary>
    public IAuditScope Replay()
    {
        var scope = Trail.BeginRequest(Info, _user);

        foreach (var entry in _entries)
        {
            scope.Record(entry);
        }

        if (Failure is not null)
        {
            scope.Fail(Failure);
        }

        return scope;
    }

    /// <summary>
    /// Does nothing, the scope returned by <see cref="Replay"/> is the one to commit.
    /// </summary>
    public ValueTask CommitAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
