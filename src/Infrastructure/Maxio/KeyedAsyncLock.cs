using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Async mutex keyed by string. Guards an in-process critical section per key (e.g. per
/// customer + plan) so that concurrent requests (a double-click) cannot both create a Maxio
/// subscription. Semaphores are reference-counted and removed when no longer used.
/// </summary>
internal sealed class KeyedAsyncLock
{
    private sealed class RefCountedSemaphore
    {
        public int RefCount = 1;
        public readonly SemaphoreSlim Semaphore = new(1, 1);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, RefCountedSemaphore> _map = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        RefCountedSemaphore entry;
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out entry!))
            {
                entry = new RefCountedSemaphore();
                _map.Add(key, entry);
            }
            else
            {
                entry.RefCount++;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseEntry(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void ReleaseEntry(string key, RefCountedSemaphore entry)
    {
        lock (_gate)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                _map.Remove(key);
            }
        }

        entry.Semaphore.Release();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private RefCountedSemaphore? _entry;

        public Releaser(KeyedAsyncLock owner, string key, RefCountedSemaphore entry)
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
                _owner.ReleaseEntry(_key, entry);
            }
        }
    }
}
