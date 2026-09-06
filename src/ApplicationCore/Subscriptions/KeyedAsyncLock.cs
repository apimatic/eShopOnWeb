using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Serialises work per key within this process. Used so that two simultaneous subscribe requests
/// from the same shopper (the classic double-click) take turns, letting the second one observe the
/// subscription the first one created instead of racing it.
/// </summary>
/// <remarks>
/// This is a single-process guard. Across instances the remaining protection is the check that
/// re-reads the shopper's subscriptions immediately before creating, plus the caller-supplied
/// idempotency key that makes a specific request safe to replay.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Holders++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Forget(key);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Forget(string key)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(key, out var entry) && --entry.Holders == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Holders;
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

            _entry.Semaphore.Release();
            _owner.Forget(_key);
        }
    }
}
