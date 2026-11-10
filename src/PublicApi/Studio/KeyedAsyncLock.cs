using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Async mutual exclusion per string key. Entries are removed when nobody holds or awaits them,
/// so a long-running process does not accumulate one semaphore per key it has ever seen.
/// </summary>
public sealed class KeyedAsyncLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

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
            entry.Users++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, held: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool held)
    {
        if (held)
        {
            entry.Semaphore.Release();
        }

        lock (_entries)
        {
            if (--entry.Users == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private KeyedAsyncLock? _owner;
        private readonly string _key;
        private readonly Entry _entry;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_key, _entry, held: true);
        }
    }
}
