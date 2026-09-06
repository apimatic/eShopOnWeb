using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key within this process. Used so two simultaneous subscribe requests from the
/// same shopper queue behind each other instead of both deciding, independently, that no subscription
/// exists yet. Cross-process races are handled separately by Maxio's uniqueness token.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(),
            (_, existing) =>
            {
                existing.AddWaiter();
                return existing;
            });

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        if (entry.RemoveWaiter() == 0)
        {
            // Only drop the entry if no one else claimed it in the meantime.
            _entries.TryRemove(new System.Collections.Generic.KeyValuePair<string, Entry>(key, entry));
        }
    }

    private sealed class Entry
    {
        private int _waiters = 1;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public void AddWaiter() => Interlocked.Increment(ref _waiters);

        public int RemoveWaiter() => Interlocked.Decrement(ref _waiters);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _entry.Semaphore.Release();
            _owner.Release(_key, _entry);
        }
    }
}
