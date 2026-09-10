using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Serializes async work per string key within the process. Used to make "subscribe" safe against
/// a double-click: concurrent requests for the same subscriber run one-at-a-time, so the second
/// observes the subscription the first created instead of creating a duplicate.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                _semaphore.Release();
            }
        }
    }
}
