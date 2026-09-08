using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Provides named async mutexes so that operations that must be serialized per
/// key (e.g. subscribe per user) can be, without blocking unrelated requests.
/// Instances are thread-safe; register as a singleton.
/// </summary>
public sealed class KeyedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> WaitAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _semaphores.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(this, key, semaphore);
    }

    private void Release(string key, SemaphoreSlim semaphore)
    {
        semaphore.Release();
        if (semaphore.CurrentCount == 1)
        {
            _semaphores.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, semaphore));
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedLock _owner;
        private readonly string _key;
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        public Releaser(KeyedLock owner, string key, SemaphoreSlim semaphore)
        {
            _owner = owner;
            _key = key;
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _semaphore);
            }
        }
    }
}
