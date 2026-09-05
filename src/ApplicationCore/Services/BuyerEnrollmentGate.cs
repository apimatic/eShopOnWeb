using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes concurrent "ensure customer + subscribe" attempts for the same buyer, so that a
/// double-click (two nearly-simultaneous POST /api/subscriptions calls) can never race past the
/// "does a subscription already exist" check and create two Maxio subscriptions. Registered as
/// a singleton so the lock is shared across requests.
/// </summary>
public class BuyerEnrollmentGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string buyerReference)
    {
        var semaphore = _locks.GetOrAdd(buyerReference, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _semaphore.Release();
        }
    }
}
