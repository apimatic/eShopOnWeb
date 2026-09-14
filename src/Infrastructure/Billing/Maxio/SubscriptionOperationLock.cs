using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class SubscriptionOperationLock
{
    private readonly SemaphoreSlim[] _locks = CreateLocks();

    public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var index = (int)(BitConverter.ToUInt32(hash, 0) % (uint)_locks.Length);
        var operationLock = _locks[index];
        await operationLock.WaitAsync(cancellationToken);
        return new Releaser(operationLock);
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
        private readonly SemaphoreSlim _operationLock;

        public Releaser(SemaphoreSlim operationLock)
        {
            _operationLock = operationLock;
        }

        public void Dispose()
        {
            _operationLock.Release();
        }
    }
}
