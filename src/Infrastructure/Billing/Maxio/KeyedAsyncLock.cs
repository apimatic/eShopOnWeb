using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises work per key within one process.
///
/// This is an optimisation, not the correctness guarantee: it collapses the common double-click
/// into a single round trip instead of two racing signups. Correctness across processes and
/// instances comes from the unique references the billing system enforces.
/// </summary>
internal sealed class KeyedAsyncLock
{
    // Bookkeeping is done under a plain lock rather than a concurrent dictionary: reference
    // counting and removal have to be one atomic step, or a departing holder can evict an entry a
    // newly arrived waiter is already using, and the two would stop excluding each other.
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseWaiter(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseWaiter(key, entry);
    }

    /// <summary>Drops the entry once nobody holds or waits on it, so the map does not grow by one permanent entry per subscriber.</summary>
    private void ReleaseWaiter(string key, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.Waiters == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Holders plus waiters. Only ever touched under the owner's gate.</summary>
        public int Waiters { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }
        }
    }
}
