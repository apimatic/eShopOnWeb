using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionKeyedLock
{
    private readonly ConcurrentDictionary<string, LockEntry> _entries = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var entry = _entries.AddOrUpdate(
            key,
            _ => new LockEntry(),
            (_, existing) =>
            {
                Interlocked.Increment(ref existing.ReferenceCount);
                return existing;
            });

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

        if (Interlocked.Decrement(ref entry.ReferenceCount) == 0 &&
            ((ICollection<KeyValuePair<string, LockEntry>>)_entries).Remove(
                new KeyValuePair<string, LockEntry>(key, entry)))
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class LockEntry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int ReferenceCount = 1;
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SubscriptionKeyedLock _owner;
        private readonly string _key;
        private LockEntry? _entry;

        public Releaser(SubscriptionKeyedLock owner, string key, LockEntry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is not null)
            {
                _owner.ReleaseReference(_key, entry, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
