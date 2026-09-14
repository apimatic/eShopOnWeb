using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises subscribe attempts per shopper inside this process, so a double-clicked subscribe
/// button cannot run two enrollments concurrently against Maxio.
/// </summary>
/// <remarks>
/// This is the fast path only. Correctness does not depend on it: the subscribe flow also checks
/// for an existing live subscription before creating one and reconciles a rejected create by
/// looking the subscription up again, which is what keeps enrollment idempotent across processes.
/// </remarks>
public class SubscriberLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
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
