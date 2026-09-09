using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A process-wide, per-key async mutex. Used to serialize concurrent subscribe requests for the same
/// user so a double-click cannot race two "ensure customer / create subscription" flows against each
/// other. Registered as a singleton so the semaphore map is shared across requests.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _semaphore.Release();
        }
    }
}
