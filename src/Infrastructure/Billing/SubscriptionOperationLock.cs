using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public interface ISubscriptionOperationLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}

public sealed class SubscriptionOperationLock : ISubscriptionOperationLock
{
    private readonly SemaphoreSlim[] _locks = CreateLocks();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var index = (key.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % _locks.Length;
        var semaphore = _locks[index];
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private static SemaphoreSlim[] CreateLocks()
    {
        var locks = new SemaphoreSlim[64];
        for (var index = 0; index < locks.Length; index++)
        {
            locks[index] = new SemaphoreSlim(1, 1);
        }

        return locks;
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
