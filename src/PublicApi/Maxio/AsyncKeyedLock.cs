using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// An async mutual-exclusion primitive keyed by string. Serializes the check-then-create window of
/// the subscribe flow per customer reference so a double-click never creates two subscriptions.
/// Entries are reference-counted and removed when idle so the map does not grow unbounded.
/// </summary>
public sealed class AsyncKeyedLock
{
    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
        public bool Removed;
    }

    private readonly ConcurrentDictionary<string, RefCountedSemaphore> _map = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _map.GetOrAdd(key, static _ => new RefCountedSemaphore());

            lock (entry)
            {
                if (entry.Removed)
                {
                    // A concurrent releaser removed this entry after we grabbed the reference; retry
                    // with a fresh entry.
                    continue;
                }

                entry.RefCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (entry)
                {
                    entry.RefCount--;
                }

                throw;
            }

            return new Releaser(this, key, entry);
        }
    }

    private void Release(string key, RefCountedSemaphore entry)
    {
        entry.Semaphore.Release();

        lock (entry)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 && !entry.Removed)
            {
                entry.Removed = true;
                _map.TryRemove(new KeyValuePair<string, RefCountedSemaphore>(key, entry));
            }
        }
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly AsyncKeyedLock _owner;
        private readonly string _key;
        private readonly RefCountedSemaphore _entry;
        private bool _disposed;

        public Releaser(AsyncKeyedLock owner, string key, RefCountedSemaphore entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _owner.Release(_key, _entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
