using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Serializes operations for a single order (or buyer) inside this process. This is the
/// first line of defence for idempotency; the Finix idempotency ids stored on the payment
/// are the second line of defence if a request is retried after a crash.
/// </summary>
public sealed class PaymentOperationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            semaphore?.Release();
        }
    }
}
