using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key across a fixed set of semaphores.
/// <para>
/// Used to collapse a double-clicked subscribe into a single round trip: the second request waits
/// for the first, then sees the subscription it created. Striping keeps memory bounded and needs no
/// reference counting; two keys sharing a stripe merely serialise unnecessarily, which is harmless.
/// </para>
/// <para>
/// This is a single-process guard. Correctness across processes does not depend on it - it comes
/// from the unique references Maxio enforces - but it saves a wasted create attempt in the common case.
/// </para>
/// </summary>
internal sealed class StripedAsyncLock
{
    private readonly SemaphoreSlim[] _stripes;

    public StripedAsyncLock(int stripeCount = 64)
    {
        if (stripeCount <= 0) throw new ArgumentOutOfRangeException(nameof(stripeCount));

        _stripes = new SemaphoreSlim[stripeCount];
        for (var i = 0; i < stripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var index = (uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_stripes.Length;
        var stripe = _stripes[index];

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
