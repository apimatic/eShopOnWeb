using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key inside this process. Used to collapse a shopper's concurrent subscribe attempts
/// (the classic double-click) into one, so the "does a live subscription already exist" check cannot be
/// overtaken by a second request that has not created its subscription yet.
/// <para>
/// This is a local optimisation only — correctness across multiple instances comes from the deterministic
/// subscription reference, which the billing provider enforces as unique.
/// </para>
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Acquires the lock for <paramref name="key"/>; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
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
        if (acquired)
        {
            entry.Semaphore.Release();
        }

        lock (_entries)
        {
            entry.Waiters--;
            if (entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
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
