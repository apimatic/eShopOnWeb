using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises enrolment work per subscriber inside this process, so a double-clicked "Subscribe"
/// runs its "does one already exist?" check and its create as one step rather than interleaving
/// two of them.
/// </summary>
/// <remarks>
/// Locks are striped over a fixed set of semaphores keyed by the subscriber's hash, so the table
/// cannot grow without bound; unrelated subscribers occasionally share a stripe, which only ever
/// costs a little serialisation. This guards a single process only - correctness across instances
/// comes from the checks made against Maxio itself, which is the system of record.
/// </remarks>
public sealed class SubscriberGate : IDisposable
{
    private const int StripeCount = 64;

    private readonly SemaphoreSlim[] _stripes;

    public SubscriberGate()
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
        var hash = StringComparer.Ordinal.GetHashCode(key);
        return (int)((uint)hash % StripeCount);
    }

    public void Dispose()
    {
        foreach (var stripe in _stripes)
        {
            stripe.Dispose();
        }
    }

    private sealed class Release : IDisposable
    {
        private SemaphoreSlim? _stripe;

        public Release(SemaphoreSlim stripe) => _stripe = stripe;

        public void Dispose() => Interlocked.Exchange(ref _stripe, null)?.Release();
    }
}
