using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises work per key inside one process.
/// <para>
/// This is a latency optimisation for the common double-click, not the correctness boundary: a
/// second concurrent subscribe for the same shopper waits here instead of racing to Maxio and
/// coming back with a 422. Across processes the guarantee still comes from the uniqueness Maxio
/// enforces on customer and subscription references, which this lock does not replace.
/// </para>
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
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
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Forget(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        Forget(key, entry);
    }

    /// <summary>
    /// Drops one claim on the entry and removes it once nobody holds or waits on it, so the map
    /// does not keep a semaphore for every shopper that has ever subscribed.
    /// </summary>
    private void Forget(string key, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        /// <summary>Number of callers holding or queued on this entry. Guarded by the owner's gate.</summary>
        public int Waiters;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);
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
