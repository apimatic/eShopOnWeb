using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationLock
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var index = (key.GetHashCode() & int.MaxValue) % _locks.Length;
        var semaphore = _locks[index];
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
