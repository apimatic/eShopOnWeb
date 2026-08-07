using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Serialises pay/refund operations per order within this process. Combined with the order's
/// persisted payment state, this makes those operations idempotent in effect: two concurrent
/// "double-click" requests are serialised, and the second sees the order already Paid/Refunded and
/// short-circuits, so PayPal is never asked to charge or refund twice.
/// </summary>
public sealed class OrderPaymentLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
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
            if (_released)
            {
                return;
            }
            _released = true;
            _semaphore.Release();
        }
    }
}
