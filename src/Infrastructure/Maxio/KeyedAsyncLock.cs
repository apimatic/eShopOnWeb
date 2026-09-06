using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key within this process.
/// </summary>
/// <remarks>
/// Used to close the check-then-create window when a shopper double-clicks subscribe: the second
/// request waits, then sees the subscription the first one created. This is a single-instance
/// guarantee only — correctness across instances comes from re-reading the shopper's subscriptions in
/// Maxio and from the <c>uniqueness_token</c> carried by every write.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Acquires the lock for <paramref name="key"/>; dispose the result to release it.</summary>
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
            Release(key, entry, releaseSemaphore: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (_gate)
        {
            // Waiters is only ever mutated under _gate, so reaching zero here means nobody else holds
            // or is queued on this entry and it is safe to drop.
            if (--entry.Waiters == 0 &&
                _entries.TryGetValue(key, out var current) &&
                ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
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
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.Release(_key, _entry, releaseSemaphore: true);
        }
    }
}
