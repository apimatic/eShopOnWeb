using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class AsyncKeyedLocker
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, static _ => new Entry());
            lock (entry)
            {
                if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(entry, current))
                {
                    continue;
                }

                entry.ReferenceCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Releaser(this, key, entry);
            }
            catch
            {
                ReleaseReference(key, entry);
                throw;
            }
        }
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(string key, Entry entry)
    {
        lock (entry)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                ((ICollection<KeyValuePair<string, Entry>>)_entries)
                    .Remove(new KeyValuePair<string, Entry>(key, entry));
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        internal readonly SemaphoreSlim Semaphore = new(1, 1);
        internal int ReferenceCount;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly AsyncKeyedLocker _owner;
        private readonly string _key;
        private Entry? _entry;

        internal Releaser(AsyncKeyedLocker owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is not null)
            {
                _owner.Release(_key, entry);
            }
        }
    }
}
