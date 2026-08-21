using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class AsyncKeyedLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, static _ => new Entry());
            Interlocked.Increment(ref entry.References);

            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(entry, current))
            {
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

            ReleaseReference(key, entry);
        }
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(string key, Entry entry)
    {
        if (Interlocked.Decrement(ref entry.References) == 0)
        {
            ((ICollection<KeyValuePair<string, Entry>>)_entries)
                .Remove(new KeyValuePair<string, Entry>(key, entry));
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly AsyncKeyedLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _released;

        public Releaser(AsyncKeyedLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }
        }
    }
}
