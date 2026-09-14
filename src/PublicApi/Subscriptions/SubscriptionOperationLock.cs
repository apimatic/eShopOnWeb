using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationLock
{
    private readonly SemaphoreSlim[] _locks = CreateLocks();

    public async ValueTask<IAsyncDisposable> AcquireAsync(string userId, CancellationToken cancellationToken)
    {
        var index = (int)((uint)StringComparer.Ordinal.GetHashCode(userId) % _locks.Length);
        var operationLock = _locks[index];
        await operationLock.WaitAsync(cancellationToken);
        return new Releaser(operationLock);
    }

    private static SemaphoreSlim[] CreateLocks()
    {
        var locks = new SemaphoreSlim[64];
        for (var index = 0; index < locks.Length; index++)
            locks[index] = new SemaphoreSlim(1, 1);
        return locks;
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _operationLock;

        public Releaser(SemaphoreSlim operationLock)
        {
            _operationLock = operationLock;
        }

        public ValueTask DisposeAsync()
        {
            _operationLock.Release();
            return ValueTask.CompletedTask;
        }
    }
}
