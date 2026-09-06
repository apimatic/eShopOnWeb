using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key inside this process.
/// </summary>
/// <remarks>
/// Used to funnel concurrent signups for the same user through one at a time, so the
/// "does a subscription already exist?" check cannot be overtaken by a sibling request in the
/// same instance. It is deliberately process-local: across instances the provider-side
/// uniqueness token and the customer/plan check are what keep signups single.
/// </remarks>
internal sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
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
                // A concurrent release may have evicted this entry between GetOrAdd and the lock.
                if (entry.Waiters >= 0)
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
            if (--entry.Waiters == 0)
            {
                entry.Waiters = -1;
                _entries.TryRemove(new System.Collections.Generic.KeyValuePair<string, Entry>(key, entry));
            }
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
    }

    private sealed class Release : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Release(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _entry.Semaphore.Release();
            _owner.Return(_key, _entry);
        }
    }
}
