using HotChocolate.Fusion.Authorization;

namespace HotChocolate.Fusion;

/// <summary>
/// A policy that allows or denies every entry it is asked about, depending on a switch the test flips.
/// </summary>
public sealed class SwitchablePolicy(string name, bool reevaluatesPerEvent) : IPolicy, IPolicyProvider
{
    private int _allowed = 1;

    public bool Allowed
    {
        get => Volatile.Read(ref _allowed) == 1;
        set => Volatile.Write(ref _allowed, value ? 1 : 0);
    }

    public Exception? Fault { get; set; }

    public bool ReevaluatesPerEvent => reevaluatesPerEvent;

    public IPolicy? GetPolicy(string policyName, string directiveName)
        => policyName == name ? this : null;

    public ValueTask EvaluateAsync(
        PolicyEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (Fault is { } fault)
        {
            throw fault;
        }

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
