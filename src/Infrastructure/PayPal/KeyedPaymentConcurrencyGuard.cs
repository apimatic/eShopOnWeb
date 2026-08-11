using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// A process-wide keyed lock that serializes payment operations on the same order. Registered as a
/// singleton so all requests share the same key set. This makes a double-click safe within a single
/// host (the in-memory run); a distributed deployment would swap this for a distributed lock.
/// </summary>
public class KeyedPaymentConcurrencyGuard : IPaymentConcurrencyGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Release();
        }
    }
}
