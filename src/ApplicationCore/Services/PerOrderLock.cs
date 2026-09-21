using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes payment operations per order within the process, so two concurrent requests on the same
/// order (a double-click) cannot both authorize/capture/refund it. Combined with the order status guards
/// and the deterministic PayPal-Request-Id keys, this keeps payment operations idempotent in effect.
/// Registered as a singleton. (This guards a single process; the app runs on one in-memory host per the
/// deployment notes. A multi-instance deployment would replace this with a distributed lock.)
/// </summary>
public sealed class PerOrderLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
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
