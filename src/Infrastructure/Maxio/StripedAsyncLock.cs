using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A small, fixed-size set of async locks addressed by key hash. Used to serialise concurrent
/// subscribe attempts for the same shopper (the classic double-click) so the second attempt observes
/// what the first one created rather than racing it.
///
/// This is an in-process guard only; correctness across instances comes from the checks performed
/// against Maxio itself, which is the system of record.
/// </summary>
public sealed class StripedAsyncLock
{
    private readonly SemaphoreSlim[] _stripes;

    public StripedAsyncLock(int stripeCount = 128)
    {
        if (stripeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stripeCount));
        }

        _stripes = new SemaphoreSlim[stripeCount];
        for (var i = 0; i < stripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var index = (int)((uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_stripes.Length);
        var stripe = _stripes[index];
        await stripe.WaitAsync(cancellationToken);

        return new Releaser(stripe);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
