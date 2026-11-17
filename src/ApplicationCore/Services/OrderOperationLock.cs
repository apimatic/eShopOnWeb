using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes payment operations for the same order inside this application instance.
/// Finix idempotency keys remain the cross-instance safety net.
/// </summary>
public class OrderOperationLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

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
