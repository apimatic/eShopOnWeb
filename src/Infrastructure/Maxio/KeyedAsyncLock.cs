using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key inside this process. Used so that a shopper's concurrent subscribe
/// attempts (the classic double click) queue up instead of racing each other into the billing
/// provider. It is an optimisation, not the correctness mechanism: idempotency is ultimately
/// enforced by the reference lookups and the provider's uniqueness constraint, which also hold
/// across processes.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_entries)
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
            Forget(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Forget(string key, Entry entry)
    {
        lock (_entries)
        {
            entry.WaiterCount--;
            if (entry.WaiterCount == 0)
            {
                _entries.Remove(key);
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
        private bool _released;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
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
            _owner.Forget(_key, _entry);
        }
    }
}
