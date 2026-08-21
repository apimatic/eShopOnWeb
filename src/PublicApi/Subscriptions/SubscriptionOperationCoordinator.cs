using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationCoordinator
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

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

                entry.ReferenceCount++;
                break;
            }
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void Release(string key, LockEntry entry)
    {
        entry.Gate.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(string key, LockEntry entry)
    {
        lock (entry)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                entry.Removed = true;
                ((ICollection<KeyValuePair<string, LockEntry>>)_locks)
                    .Remove(new KeyValuePair<string, LockEntry>(key, entry));
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
        public bool Removed { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private SubscriptionOperationCoordinator? _owner;
        private readonly string _key;
        private readonly LockEntry _entry;

        public Releaser(SubscriptionOperationCoordinator owner, string key, LockEntry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_key, _entry);
        }
    }
}
