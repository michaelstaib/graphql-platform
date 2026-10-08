using System.Net;
using System.Runtime.CompilerServices;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Nodes;

namespace HotChocolate.Fusion.Authorization;

/// <summary>
/// Ends the events of a subscription gracefully when the token of the principal expires.
/// </summary>
internal sealed class ExpiringEventStream(
    IAsyncEnumerable<EventMessageResult> events,
    IExecutionResult result,
    SubscriptionAuthorization authorization,
    RequestContext context,
    DateTimeOffset expiry,
    TimeProvider timeProvider)
    : IAsyncEnumerable<EventMessageResult>
{
    public IAsyncEnumerator<EventMessageResult> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(events, result, authorization, context, expiry, timeProvider, cancellationToken);

    private sealed class Enumerator : IAsyncEnumerator<EventMessageResult>
    {
        private readonly IExecutionResult _result;
        private readonly SubscriptionAuthorization _authorization;
        private readonly RequestContext _context;
        private readonly TimeProvider _timeProvider;
        private readonly CancellationToken _callerToken;
        private readonly CancellationTokenSource _expired = new();
        private readonly CancellationTokenSource _linked;
        private readonly IAsyncEnumerator<EventMessageResult> _inner;
        private readonly ITimer _timer;
        private bool _recorded;

        public Enumerator(
            IAsyncEnumerable<EventMessageResult> events,
            IExecutionResult result,
            SubscriptionAuthorization authorization,
            RequestContext context,
            DateTimeOffset expiry,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            _result = result;
            _authorization = authorization;
            _context = context;
            _timeProvider = timeProvider;
            _callerToken = cancellationToken;
            _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _expired.Token);
            _inner = events.GetAsyncEnumerator(_linked.Token);
            _timer = timeProvider.CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Arm(expiry);
        }

        public EventMessageResult Current => _inner.Current;

        private bool IsExpired => _expired.IsCancellationRequested && !_callerToken.IsCancellationRequested;

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public async ValueTask<bool> MoveNextAsync()
        {
            bool hasNext;

            try
            {
                hasNext = await _inner.MoveNextAsync();
            }
            catch (OperationCanceledException) when (IsExpired)
            {
                hasNext = false;
            }

            if (!hasNext && IsExpired && !_recorded)
            {
                _recorded = true;
                _result.ContextData = _result.ContextData.SetItem(
                    ExecutionContextData.HttpStatusCode,
                    HttpStatusCode.Unauthorized);
                await _authorization.RecordExpiryAsync(_context, _callerToken);
            }

            return hasNext;
        }

        public async ValueTask DisposeAsync()
        {
            await _timer.DisposeAsync();
            await _inner.DisposeAsync();
            _linked.Dispose();
            _expired.Dispose();
        }

        private void Arm(DateTimeOffset expiry)
        {
            var remaining = expiry - _timeProvider.GetUtcNow();

            if (remaining <= TimeSpan.Zero)
            {
                _expired.Cancel();
                return;
            }

            _timer.Change(remaining, Timeout.InfiniteTimeSpan);
        }

        private void OnTimer(object? state)
        {
            try
            {
                var current = _authorization.GetCurrentExpiry(_context);

                if (current is { } renewed && renewed > _timeProvider.GetUtcNow())
                {
                    Arm(renewed);
                    return;
                }

                _expired.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
