using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class SubscriptionKeyLock
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        LockEntry entry;
        while (true)
        {
            entry = _locks.GetOrAdd(key, static _ => new LockEntry());
            lock (entry)
            {
                if (entry.Removed)
                {
                    continue;
                }

                entry.Users++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void ReleaseReference(string key, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (entry)
        {
            entry.Users--;
            if (entry.Users == 0 && _locks.TryRemove(new KeyValuePair<string, LockEntry>(key, entry)))
            {
                entry.Removed = true;
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
        public bool Removed { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SubscriptionKeyLock _owner;
        private readonly string _key;
        private LockEntry? _entry;

        public Releaser(SubscriptionKeyLock owner, string key, LockEntry entry)
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
                _owner.ReleaseReference(_key, entry, releaseSemaphore: true);
            }
        }
    }
}
