using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serializes work per key within this process, so that two simultaneous requests for the same
/// subscriber cannot both decide "no subscription exists yet" and each create one.
/// </summary>
/// <remarks>
/// This closes the double-click window on a single node. Correctness across nodes does not depend
/// on it: the customer record is deduplicated by Maxio's uniqueness constraint on
/// <c>reference</c>, and a retried enrollment is deduplicated by <c>uniqueness_token</c>.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, acquired: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool acquired)
    {
        lock (_entries)
        {
            entry.Waiters--;
            if (entry.Waiters == 0)
            {
                _entries.Remove(key);
            }
        }

        if (acquired)
        {
            entry.Semaphore.Release();
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

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
                _owner.Release(_key, _entry, acquired: true);
            }
        }
    }
}
