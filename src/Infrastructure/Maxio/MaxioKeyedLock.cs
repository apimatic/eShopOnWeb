using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes work per key so two concurrent requests for the same customer cannot both decide that
/// nothing exists yet and both create it — the double-clicked Subscribe button.
/// </summary>
/// <remarks>
/// Striped rather than one semaphore per key, so the memory is bounded by the stripe count instead of
/// growing with the number of users ever seen. Two customers that collide on a stripe are serialized
/// unnecessarily, which is harmless. This guards a single process; a multi-instance deployment would
/// additionally rely on Maxio's own one-customer-per-reference rule, which is what the create/re-read
/// recovery path in the billing service is there to handle.
/// </remarks>
public sealed class MaxioKeyedLock
{
    private readonly SemaphoreSlim[] _stripes;

    public MaxioKeyedLock(int stripeCount = 64)
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

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var stripe = _stripes[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_stripes.Length];
        await stripe.WaitAsync(cancellationToken);
        return new Release(stripe);
    }

    private sealed class Release : IDisposable
    {
        private SemaphoreSlim? _stripe;

        public Release(SemaphoreSlim stripe) => _stripe = stripe;

        public void Dispose() => Interlocked.Exchange(ref _stripe, null)?.Release();
    }
}
