using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key inside this process.
/// </summary>
/// <remarks>
/// Used to collapse a shopper's concurrent subscribe attempts — the classic double-click — into one
/// sequence of billing calls, so the "does a live subscription already exist?" check cannot be raced by
/// the request sitting next to it. It is deliberately process-local: across replicas the billing system's
/// own uniqueness constraint on the customer reference plus the pre-flight subscription check remain the
/// backstop.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

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

        return new Releaser(this, key, entry);
    }

    private Entry Rent(string key)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());

            lock (entry)
            {
                // The entry may have been retired by a concurrent release between GetOrAdd and this lock.
                if (!entry.Retired)
                {
                    entry.Users++;
                    return entry;
                }
            }
        }
    }

    private void Return(string key, Entry entry)
    {
        lock (entry)
        {
            entry.Users--;
            if (entry.Users > 0)
            {
                return;
            }

            // Retire and unpublish under the same lock so a concurrent Rent either sees a live entry
            // (and joins it) or fails to find one at all, never spinning on a retired-but-published entry.
            entry.Retired = true;
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }

        entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }

        public bool Retired { get; set; }
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
            _owner.Return(_key, _entry);
        }
    }
}
