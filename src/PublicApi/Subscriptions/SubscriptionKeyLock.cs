using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionKeyLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(),
            (_, existing) =>
            {
                Interlocked.Increment(ref existing.References);
                return existing;
            });

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Release(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void ReleaseReference(string key, Entry entry)
    {
        if (Interlocked.Decrement(ref entry.References) == 0)
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References = 1;
    }

    private sealed class Release(SubscriptionKeyLock owner, string key, Entry entry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            entry.Semaphore.Release();
            owner.ReleaseReference(key, entry);
            _disposed = true;
        }
    }
}
