using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises work per key inside this process.
/// </summary>
/// <remarks>
/// Used to collapse a shopper's concurrent subscribe requests — the impatient double-click — so the
/// "does this customer already have this subscription?" check and the create that follows cannot
/// interleave. It is deliberately process-local: across hosts the guarantee comes from the
/// deterministic references Maxio enforces as unique, not from this lock.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Acquires the lock for <paramref name="key"/>; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
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
                // A concurrent release may have retired this entry between the lookup and the lock.
                if (!entry.Retired)
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
            if (entry.Waiters > 0)
            {
                return;
            }

            entry.Retired = true;
        }

        _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Waiters { get; set; }

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
