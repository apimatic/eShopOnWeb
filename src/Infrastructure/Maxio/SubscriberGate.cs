using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises concurrent subscribe attempts for the same shopper within this process.
/// </summary>
/// <remarks>
/// This is the fast path of the idempotency story, not the guarantee. It turns the common double
/// click — two requests landing on one instance milliseconds apart — into one Maxio write and one
/// cheap "you are already subscribed" answer, instead of a race that both sides discover only after
/// paying for a round trip. Correctness across instances and restarts comes from the deterministic
/// references Maxio enforces uniqueness on; this gate merely keeps the common case quiet.
/// </remarks>
public class SubscriberGate : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Acquires the gate for <paramref name="key"/>. Dispose the returned handle to release it.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Return(key, entry);
            throw;
        }

        return new Handle(this, key, entry);
    }

    private Entry Rent(string key)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());

            lock (entry)
            {
                // A concurrent release may have already retired this entry; if so, retry the lookup
                // so we never wait on a semaphore that is no longer the one others are queueing on.
                if (!entry.Retired)
                {
                    entry.Waiters++;
                    return entry;
                }
            }
        }
    }

    private void Return(string key, Entry entry)
    {
        lock (entry)
        {
            entry.Waiters--;
            if (entry.Waiters > 0)
            {
                return;
            }

            entry.Retired = true;
        }

        _entries.TryRemove(new System.Collections.Generic.KeyValuePair<string, Entry>(key, entry));
        entry.Semaphore.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var pair in _entries)
        {
            if (_entries.TryRemove(pair))
            {
                pair.Value.Semaphore.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Waiters { get; set; }

        public bool Retired { get; set; }
    }

    private sealed class Handle : IDisposable
    {
        private readonly SubscriberGate _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private bool _released;

        public Handle(SubscriberGate owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _entry.Semaphore.Release();
            _owner.Return(_key, _entry);
        }
    }
}
