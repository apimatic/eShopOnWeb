using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes concurrent operations for the same user within this process, so that e.g. a
/// double-click on "Subscribe" cannot race past the check-then-create idempotency guard and
/// create two subscriptions.
/// </summary>
internal interface IUserOperationLock
{
    Task<IDisposable> AcquireAsync(string userId, CancellationToken cancellationToken);
}

internal class UserOperationLock : IUserOperationLock
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string userId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

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
