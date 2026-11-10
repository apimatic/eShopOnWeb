using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Serialises work per key (in-process). Entries are removed when no one uses them any more,
/// so a long-running process does not accumulate locks.
/// </summary>
public class KeyedLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    private readonly Dictionary<string, Entry> _entries = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }
            entry.Users++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct);
        }
        catch
        {
            Release(key, entry, false);
            throw;
        }
        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool held)
    {
        if (held) entry.Semaphore.Release();
        lock (_entries)
        {
            if (--entry.Users == 0) _entries.Remove(key);
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(KeyedLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _owner.Release(_key, _entry, true);
        }
    }
}
