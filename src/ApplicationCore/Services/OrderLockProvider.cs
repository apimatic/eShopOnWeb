using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes the payment operations that touch one order, so a double-click cannot run two
/// authorizations (or two captures) concurrently and have both pass their state check.
///
/// This is an in-process guard, and honest about it: it holds within one host, which is what the
/// double-click case needs. It is not a distributed lock — across hosts, correctness rests on the
/// persisted state machine plus the deterministic idempotency keys sent to the processor.
/// </summary>
public sealed class OrderLockProvider
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Release(semaphore);
    }

    private sealed class Release : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Release(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
