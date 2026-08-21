using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class AsyncKeyedLocker
{
    private const int StripeCount = 128;
    private readonly SemaphoreSlim[] _stripes = CreateStripes();

    public async ValueTask<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var index = (StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % StripeCount;
        var semaphore = _stripes[index];
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private static SemaphoreSlim[] CreateStripes()
    {
        var stripes = new SemaphoreSlim[StripeCount];
        for (var index = 0; index < stripes.Length; index++)
        {
            stripes[index] = new SemaphoreSlim(1, 1);
        }

        return stripes;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
