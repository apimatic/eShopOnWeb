using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class SubscriptionOperationLocks
{
    private readonly SemaphoreSlim[] _stripes = CreateStripes();

    public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var stripe = _stripes[(uint)StringComparer.Ordinal.GetHashCode(key) % _stripes.Length];
        await stripe.WaitAsync(cancellationToken);
        return new Releaser(stripe);
    }

    private static SemaphoreSlim[] CreateStripes()
    {
        var stripes = new SemaphoreSlim[64];
        for (var i = 0; i < stripes.Length; i++)
        {
            stripes[i] = new SemaphoreSlim(1, 1);
        }

        return stripes;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
