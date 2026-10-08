namespace HotChocolate.Fusion.Authorization;

internal sealed class ToggledPolicy(string name, bool reevaluatesPerEvent) : IPolicy, IPolicyProvider
{
    private int _allowed = 1;
    private int _evaluations;

    public bool Allowed
    {
        get => Volatile.Read(ref _allowed) == 1;
        set => Volatile.Write(ref _allowed, value ? 1 : 0);
    }

    public int Evaluations => Volatile.Read(ref _evaluations);

    public bool ReevaluatesPerEvent => reevaluatesPerEvent;

    public IPolicy? GetPolicy(string policyName, string directiveName)
        => policyName == name ? this : null;

    public ValueTask EvaluateAsync(
        PolicyEvaluationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _evaluations);

        foreach (ref readonly var entry in context.Entries)
        {
            if (Allowed)
            {
                context.Allow(in entry);
            }
            else
            {
                context.Deny(in entry, "revoked");
            }
        }

        return ValueTask.CompletedTask;
    }
}
