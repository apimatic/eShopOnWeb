using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises work per key within this process, so two simultaneous requests from the same shopper
/// take turns instead of racing to create the same billing record.
/// </summary>
/// <remarks>
/// This is a latency optimisation for the common case - an impatient double-click landing on one
/// instance - not the correctness boundary. Correctness comes from the read-before-write check and
/// the uniqueness token sent to Maxio, both of which hold across instances and restarts.
/// </remarks>
internal sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    // Guards both the dictionary and the reference counts, so an entry can never be evicted while
    // another caller is queueing on it.
    private readonly object _gate = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.WaiterCount++;
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

    private void ReleaseWaiter(string key, Entry entry)
    {
        lock (_gate)
        {
            // Drop the entry once nobody holds or is queued on it, so the dictionary does not grow by
            // one permanent entry per shopper who has ever subscribed.
            if (--entry.WaiterCount == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int WaiterCount { get; set; }
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
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _entry.Semaphore.Release();
            _owner.ReleaseWaiter(_key, _entry);
        }
    }
}
