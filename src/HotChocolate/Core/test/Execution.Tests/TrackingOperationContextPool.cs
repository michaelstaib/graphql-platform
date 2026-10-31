using HotChocolate.Execution.Processing;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Execution;

internal sealed class TrackingOperationContextPool
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private readonly List<bool> _reusable = [];
    private readonly SemaphoreSlim _returned = new(0);

    public IReadOnlyList<bool> Reusable
    {
        get
        {
            lock (_reusable)
            {
                return [.. _reusable];
            }
        }
    }

    public ObjectPool<OperationContext> Wrap(ObjectPool<OperationContext> inner)
        => new Pool(this, inner);

    public async Task WaitForReturnsAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!await _returned.WaitAsync(s_timeout, TestContext.Current.CancellationToken))
            {
                throw new TimeoutException("A context was not returned to its pool in time.");
            }
        }
    }

    private sealed class Pool(TrackingOperationContextPool owner, ObjectPool<OperationContext> inner)
        : ObjectPool<OperationContext>
    {
        public override OperationContext Get() => inner.Get();

        public override void Return(OperationContext obj)
        {
            inner.Return(obj);

            lock (owner._reusable)
            {
                // a context the pool accepted for reuse was cleaned.
                owner._reusable.Add(!obj.IsInitialized);
            }

            owner._returned.Release();
        }
    }
}
