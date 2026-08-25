using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class SubscriptionKeyLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(),
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

    private void ReleaseReference(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        if (Interlocked.Decrement(ref entry.ReferenceCount) == 0)
        {
            ((ICollection<KeyValuePair<string, Entry>>)_entries)
                .Remove(new KeyValuePair<string, Entry>(key, entry));
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        public int ReferenceCount = 1;
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SubscriptionKeyLock _owner;
        private readonly string _key;
        private Entry? _entry;

        public Releaser(SubscriptionKeyLock owner, string key, Entry entry)
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
