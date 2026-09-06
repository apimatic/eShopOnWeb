using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Serialises work per key within this process.
/// </summary>
/// <remarks>
/// <para>
/// Used to funnel a shopper's concurrent subscribe attempts -- an impatient double-click, a client
/// retry -- through the check-then-create sequence one at a time, so the second attempt sees the
/// subscription the first one made.
/// </para>
/// <para>
/// It is process-local, so it does not by itself protect a multi-instance deployment. That case is
/// covered by the deterministic subscription reference and the uniqueness constraint Maxio enforces on
/// it, which turns a lost race into a rejected duplicate rather than a second subscription.
/// </para>
/// <para>
/// Entries are reference counted and dropped once nobody is waiting, so a long-lived instance does not
/// accumulate one semaphore per user ever seen.
/// </para>
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>
    /// Acquires the lock for <paramref name="key"/>, releasing it when the returned handle is disposed.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Return(key, entry, releaseSlot: false);
            throw;
        }

        return new Handle(this, key, entry);
    }

    private Entry Rent(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Waiters++;
            return entry;
        }
    }

    private void Return(string key, Entry entry, bool releaseSlot)
    {
        lock (_gate)
        {
            if (releaseSlot)
            {
                entry.Semaphore.Release();
            }

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

        /// <summary>Callers holding or waiting for the lock. Guarded by the owning lock's gate.</summary>
        public int Waiters { get; set; }
    }

    private sealed class Handle : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Handle(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Return(_key, _entry, releaseSlot: true);
            }
        }
    }
}
