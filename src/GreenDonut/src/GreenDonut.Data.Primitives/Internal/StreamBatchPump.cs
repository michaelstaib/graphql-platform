using System.Runtime.ExceptionServices;

namespace GreenDonut.Data.Internal;

/// <summary>
/// Demultiplexes one flat, key-ordered row stream into a <see cref="StreamPagePump{TElement}"/>
/// per requested key, and releases the source and the lifetime once every requested key's page
/// has completed or been disposed.
/// </summary>
/// <typeparam name="TKey">
/// The type of the key that routes a row to its page.
/// </typeparam>
/// <typeparam name="TElement">
/// The type of the source rows.
/// </typeparam>
/// <remarks>
/// Rows arrive grouped by key. A row whose key differs from the previous row's key completes the
/// previous key's page; source exhaustion completes every page already created, and leaves a
/// still-unbuilt key's channel to complete on its own first pull. This way a requested key that
/// never appears in the stream, and whose page is created only after the source ran out, still
/// completes as an empty page. Pulling on any key's page drives this pump; rows for other keys
/// are buffered into their own pages. A key whose page was disposed before it completed is
/// abandoned: the pump keeps advancing past its remaining rows but discards them instead of
/// buffering them. A row for a key whose run already completed and was not abandoned means the
/// source is not grouped by key, which faults the pump exactly like a mid-stream source
/// exception. This type has no cross-thread safety, the same stance as
/// <see cref="StreamPageBuffer{TElement}"/>.
/// </remarks>
internal sealed class StreamBatchPump<TKey, TElement>
    where TKey : notnull
{
    private readonly IAsyncEnumerator<StreamBatchRow<TKey, TElement>> _source;
    private readonly Dictionary<TKey, KeyChannel> _keys;
    private IAsyncDisposable? _lifetime;
    private int _liveKeys;
    private bool _hasCurrentKey;
    private TKey _currentKey = default!;
    private bool _released;
    private bool _sourceExhausted;
    private ExceptionDispatchInfo? _fault;

    private StreamBatchPump(
        IAsyncEnumerator<StreamBatchRow<TKey, TElement>> source,
        IReadOnlyCollection<TKey> keys,
        IAsyncDisposable? lifetime)
    {
        _source = source;
        _lifetime = lifetime;
        _keys = new Dictionary<TKey, KeyChannel>(keys.Count);

        foreach (var key in keys)
        {
            if (!_keys.TryAdd(key, new KeyChannel()))
            {
                throw ThrowHelper.StreamBatchPump_DuplicateKey(key);
            }
        }

        _liveKeys = _keys.Count;
    }

    /// <summary>
    /// Creates a batch pump for the given requested keys, reading exactly one row from
    /// <paramref name="source"/> and parking it in whichever requested key sorts first, or, for
    /// an empty key set, disposes <paramref name="source"/> and <paramref name="lifetime"/>
    /// immediately and returns null.
    /// </summary>
    /// <param name="source">
    /// The shared, key-ordered source enumerator that produces rows for every requested key.
    /// </param>
    /// <param name="keys">
    /// The requested keys, which must not contain a duplicate. The returned pump releases the
    /// source and the lifetime once every one of these keys' pages has completed or been disposed.
    /// </param>
    /// <param name="lifetime">
    /// A resource owned by the pump, disposed once every key's page has completed or been
    /// disposed, or null if the pump owns nothing beyond the source.
    /// </param>
    /// <returns>
    /// Returns the batch pump, or null if <paramref name="keys"/> is empty and there is nothing
    /// to serve.
    /// </returns>
    public static async ValueTask<StreamBatchPump<TKey, TElement>?> CreateAsync(
        IAsyncEnumerator<StreamBatchRow<TKey, TElement>> source,
        IReadOnlyCollection<TKey> keys,
        IAsyncDisposable? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count > 0)
        {
            var pump = new StreamBatchPump<TKey, TElement>(source, keys, lifetime);

            try
            {
                await pump.PumpOnceAsync().ConfigureAwait(false);
            }
            catch (Exception primingException)
            {
                // the priming failure is what the caller must observe; a disposal failure while
                // releasing the source and the lifetime for it is attached instead of replacing it.
                try
                {
                    await pump.ReleaseCoreAsync().ConfigureAwait(false);
                }
                catch (Exception releaseException)
                {
                    OrderedDisposal.Attach(primingException, releaseException);
                }

                throw;
            }

            return pump;
        }

        await OrderedDisposal.ReleaseAsync(
            source.DisposeAsync,
            lifetime is null ? null : lifetime.DisposeAsync).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Builds the page for the given requested key, wiring it to complete when the shared source
    /// moves past that key. Must be called exactly once for every key this batch pump was created
    /// with, before any page is primed.
    /// </summary>
    /// <param name="key">
    /// One of the keys this batch pump was created with.
    /// </param>
    /// <param name="definition">
    /// The definition that governs how rows turn into content, flags, and a total count.
    /// </param>
    /// <param name="createCursor">
    /// Creates a cursor from a page item.
    /// </param>
    public StreamPage<TElement> CreatePage(
        TKey key,
        StreamPageDefinition<TElement> definition,
        Func<EdgeEntry<TElement>, string> createCursor)
    {
        var pump = CreateKeyPump(key);
        var page = ValueCursorStreamPage<TElement>.CreateForBatch(pump, definition, createCursor);
        RegisterDrain(key, page);
        return page;
    }

    /// <summary>
    /// Builds the page for the given requested key, projecting each source row into a different
    /// item type, and wiring it to complete when the shared source moves past that key. Must be
    /// called exactly once for every key this batch pump was created with, before any page is
    /// primed.
    /// </summary>
    /// <typeparam name="TValue">
    /// The type of the page's items.
    /// </typeparam>
    /// <param name="key">
    /// One of the keys this batch pump was created with.
    /// </param>
    /// <param name="definition">
    /// The definition that governs how rows turn into content, flags, and a total count.
    /// </param>
    /// <param name="valueSelector">
    /// Projects a source row into a page item.
    /// </param>
    /// <param name="createCursor">
    /// Creates a cursor from a source row.
    /// </param>
    public StreamPage<TValue> CreatePage<TValue>(
        TKey key,
        StreamPageDefinition<TElement> definition,
        Func<TElement, TValue> valueSelector,
        Func<EdgeEntry<TElement>, string> createCursor)
    {
        var pump = CreateKeyPump(key);
        var page = ElementCursorStreamPage<TElement, TValue>.CreateForBatch(pump, definition, valueSelector, createCursor);
        RegisterDrain(key, page);
        return page;
    }

    private StreamPagePump<TElement> CreateKeyPump(TKey key)
    {
        if (!_keys.TryGetValue(key, out var channel))
        {
            throw ThrowHelper.StreamBatchPump_KeyNotRequested(key);
        }

        if (channel.Drain is not null)
        {
            throw ThrowHelper.StreamBatchPump_KeyAlreadyHasPage(key);
        }

        return new StreamPagePump<TElement>(new KeyReader(this, key), pageCount: 1);
    }

    private void RegisterDrain<TValue>(TKey key, StreamPage<TValue> page)
        => _keys[key].Drain = cancellationToken => DrainAsync(page, cancellationToken);

    // Reads a page to completion over its public surface: every implementation buffers as it
    // enumerates, so this has the same effect as draining the page's source directly.
    private static async ValueTask DrainAsync<TValue>(
        StreamPage<TValue> page,
        CancellationToken cancellationToken)
    {
        await foreach (var _ in page.GetEntriesAsync(cancellationToken).ConfigureAwait(false))
        {
            // draining only for its buffering side effect; the entries themselves are unused here.
        }
    }

    /// <summary>
    /// The number of rows already read for <paramref name="key"/> but not yet handed to its
    /// page, for tests to observe staging that has no other externally visible effect.
    /// </summary>
    /// <param name="key">
    /// One of the keys this batch pump was created with.
    /// </param>
    internal int StagedRowCount(TKey key) => _keys[key].Rows.Count;

    // Returns the next row for the given key, reading from the shared source until one arrives,
    // the key's run completes, or the source is exhausted.
    private async ValueTask<StreamRow<TElement>?> ReadNextAsync(TKey key)
    {
        var channel = _keys[key];

        while (channel.Rows.Count == 0 && !channel.Completed)
        {
            await PumpOnceAsync().ConfigureAwait(false);
        }

        return channel.Rows.Count > 0 ? channel.Rows.Dequeue() : null;
    }

    // Advances the shared source by exactly one row, routing it to its key's channel. A row for a
    // key whose channel already completed without being abandoned means the source is not grouped
    // by key, and faults the pump before any of the completion handling below runs, so a wrongly
    // ordered row never masquerades as a new run of an already-finished key. A key change
    // completes the previously active key's page; source exhaustion completes every page already
    // created and leaves a still-unbuilt key's channel alone, so it completes on its own first
    // pull instead (that pull re-enters this method, finds the source already exhausted, and
    // falls straight into this same completion pass). Completing a channel here also drains its
    // page, so the page's own completion (and, once every key has completed or been disposed, the
    // source and the lifetime) happens without waiting for a consumer to pull the remaining
    // buffered rows.
    private async ValueTask PumpOnceAsync()
    {
        _fault?.Throw();

        bool hasNext;

        if (_sourceExhausted)
        {
            hasNext = false;
        }
        else
        {
            try
            {
                hasNext = await _source.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // a source that faults mid-stream still releases the shared source and the
                // lifetime, exactly as reaching the end of the source does, and every later pull
                // for any key rethrows the same exception instead of touching the now-disposed
                // source again. A disposal failure while releasing is attached to this fault
                // instead of replacing it.
                _fault = ExceptionDispatchInfo.Capture(ex);

                try
                {
                    await ReleaseCoreAsync().ConfigureAwait(false);
                }
                catch (Exception releaseException)
                {
                    OrderedDisposal.Attach(ex, releaseException);
                }

                throw;
            }
        }

        if (!hasNext)
        {
            _sourceExhausted = true;

            foreach (var each in _keys.Values)
            {
                if (each.Completed || each.Drain is null)
                {
                    continue;
                }

                each.Completed = true;
                await each.Drain(CancellationToken.None).ConfigureAwait(false);
            }

            return;
        }

        var row = _source.Current;

        if (!_keys.TryGetValue(row.Key, out var channel))
        {
            throw ThrowHelper.StreamBatchPump_RowForUnrequestedKey(row.Key);
        }

        if (channel.Completed && !channel.Abandoned)
        {
            // the key's run already completed (a later key was seen, or the source reached its
            // end) and it was not abandoned, so this row is a source ordering violation: it would
            // land in a queue no page will ever read again. Fault exactly like a mid-stream source
            // exception so the source and the lifetime are released and every later pull, for any
            // key, rethrows. A disposal failure while releasing is attached to this fault instead
            // of replacing it.
            var fault = ThrowHelper.StreamBatchPump_SourceNotGroupedByKey(row.Key);
            _fault = ExceptionDispatchInfo.Capture(fault);

            try
            {
                await ReleaseCoreAsync().ConfigureAwait(false);
            }
            catch (Exception releaseException)
            {
                OrderedDisposal.Attach(fault, releaseException);
            }

            throw fault;
        }

        if (_hasCurrentKey && !EqualityComparer<TKey>.Default.Equals(_currentKey, row.Key))
        {
            var previous = _keys[_currentKey];
            previous.Completed = true;

            if (previous.Drain is not null)
            {
                await previous.Drain(CancellationToken.None).ConfigureAwait(false);
            }
        }

        _hasCurrentKey = true;
        _currentKey = row.Key;

        if (!channel.Abandoned)
        {
            channel.Rows.Enqueue(new StreamRow<TElement>
            {
                Item = row.Item,
                TotalCount = row.TotalCount,
                HasMore = row.HasMore
            });
        }
    }

    // Signals that one requested key's page has completed or been disposed. A page disposed
    // before its channel completed naturally is abandoned here: its channel is marked completed
    // so the key-change and source-exhaustion handling above leave it alone, its already-staged
    // rows are dropped, and later rows for the same key are discarded as they are read instead of
    // buffered. Once every key has completed or been disposed, disposes the source and then the
    // lifetime, exactly once.
    private async ValueTask ReleaseAsync(TKey key)
    {
        var channel = _keys[key];

        if (!channel.Completed)
        {
            channel.Completed = true;
            channel.Abandoned = true;
            channel.Rows.Clear();
            channel.Drain = null;
        }

        if (--_liveKeys > 0)
        {
            return;
        }

        await ReleaseCoreAsync().ConfigureAwait(false);
    }

    // Disposes the source and then the lifetime, exactly once, however release was triggered:
    // every requested key completing or being disposed, the source faulting mid-stream, or the
    // priming read during creation failing before any page exists to reach this path otherwise.
    // The two disposals run in separate try/finally blocks so the lifetime is still disposed even
    // when disposing the source throws. When this is the only fault (nothing was already
    // recorded), the source's exception is what a caller of this method observes, with the
    // lifetime's exception attached to it rather than replacing it.
    private async ValueTask ReleaseCoreAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;

        var lifetime = _lifetime;
        _lifetime = null;

        await OrderedDisposal.ReleaseAsync(
            _source.DisposeAsync,
            lifetime is null ? null : lifetime.DisposeAsync).ConfigureAwait(false);
    }

    private sealed class KeyChannel
    {
        public Queue<StreamRow<TElement>> Rows { get; } = new();

        public bool Completed { get; set; }

        public bool Abandoned { get; set; }

        public Func<CancellationToken, ValueTask>? Drain { get; set; }
    }

    // Adapts one key's slice of the demultiplexed stream to the single-source shape a
    // StreamPagePump expects, and turns its disposal into this key's release signal.
    private sealed class KeyReader(StreamBatchPump<TKey, TElement> pump, TKey key)
        : IAsyncEnumerator<StreamRow<TElement>>
    {
        private StreamRow<TElement>? _current;

        public StreamRow<TElement> Current => _current!;

        public async ValueTask<bool> MoveNextAsync()
        {
            _current = await pump.ReadNextAsync(key).ConfigureAwait(false);
            return _current is not null;
        }

        public ValueTask DisposeAsync() => pump.ReleaseAsync(key);
    }
}
