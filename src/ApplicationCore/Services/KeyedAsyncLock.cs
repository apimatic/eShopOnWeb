using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serialises work per key within this process. Used so that two simultaneous subscribe calls for the
/// same shopper - the double-clicked button - cannot both get past the "already subscribed?" check.
/// </summary>
/// <remarks>
/// This is a single-process guard. It is the first line of defence, not the only one: the subscribe
/// flow additionally re-checks the billing provider, which is the actual system of record.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_syncRoot)
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
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, semaphoreAcquired: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool semaphoreAcquired)
    {
        if (semaphoreAcquired)
        {
            entry.Semaphore.Release();
        }

        lock (_syncRoot)
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
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Waiters { get; set; }
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
            if (_released) return;
            _released = true;
            _owner.Release(_key, _entry, semaphoreAcquired: true);
        }
    }
}
