using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class StripedSubscriptionOperationLock : ISubscriptionOperationLock, IDisposable
{
    private readonly SemaphoreSlim[] _stripes = CreateStripes(256);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var index = (key.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % _stripes.Length;
        var semaphore = _stripes[index];
        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    public void Dispose()
    {
        foreach (var stripe in _stripes)
        {
            stripe.Dispose();
        }
    }

    private static SemaphoreSlim[] CreateStripes(int count)
    {
        var stripes = new SemaphoreSlim[count];
        for (var index = 0; index < count; index++)
        {
            stripes[index] = new SemaphoreSlim(1, 1);
        }

        return stripes;
    }

    private sealed class Lease : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
