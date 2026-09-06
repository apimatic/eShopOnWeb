using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes concurrent subscribe attempts for the same customer within this process.
/// </summary>
/// <remarks>
/// "Look for an existing subscription, then create one if there is none" is read-then-write and therefore
/// not atomic, and the provider offers no uniqueness constraint to fall back on. Holding a gate per
/// customer turns the classic double-click — two requests racing through the check before either write
/// lands — into two sequential attempts, the second of which finds the subscription the first created.
/// Striped rather than per-key so the table cannot grow without bound; a collision only costs two
/// unrelated shoppers a brief wait on a call that is rare by nature.
/// </remarks>
public sealed class MaxioSubscribeGate
{
    private const int StripeCount = 64;

    private readonly SemaphoreSlim[] _stripes;

    public MaxioSubscribeGate()
    {
        _stripes = new SemaphoreSlim[StripeCount];
        for (var i = 0; i < StripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var stripe = _stripes[StripeIndex(key)];
        await stripe.WaitAsync(cancellationToken);
        return new Release(stripe);
    }

    private static int StripeIndex(string key)
    {
        // Deliberately not string.GetHashCode(): that is randomized per process, which is fine here but
        // makes behaviour harder to reason about across instances. FNV-1a is stable and cheap.
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in key)
            {
                hash = (hash ^ c) * 16777619;
            }

            return (int)(hash % StripeCount);
        }
    }

    private sealed class Release : IDisposable
    {
        private SemaphoreSlim? _stripe;

        public Release(SemaphoreSlim stripe) => _stripe = stripe;

        public void Dispose()
        {
            var stripe = Interlocked.Exchange(ref _stripe, null);
            stripe?.Release();
        }
    }
}
