using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Serialises work per key within this process. Used so that a shopper who double-clicks
/// "Subscribe" has their second request wait for - and then observe - the first one, instead of
/// both racing past the "are you already subscribed?" check.
/// </summary>
/// <remarks>
/// This is a latency optimisation, not the correctness boundary: across several instances the
/// billing provider's own duplicate guard is what prevents a second enrollment.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Return(key, entry);
            throw;
        }

        return new Release(this, key, entry);
    }

    private Entry Rent(string key)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());

            lock (entry)
            {
                // Another thread may have dropped this entry to zero and removed it between the
                // GetOrAdd and the lock; if so, start over with a fresh one.
                if ((entry.Waiters > 0) || (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)))
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
            if (entry.Waiters == 0)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Waiters { get; set; }
    }

    private sealed class Release : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private bool _released;

        public Release(KeyedAsyncLock owner, string key, Entry entry)
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
