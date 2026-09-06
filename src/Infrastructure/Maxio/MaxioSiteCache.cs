using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Caches the Maxio site settings for a short window. They change about as often as the site is
/// reconfigured, and re-reading them on every subscribe would add a round trip to the hot path.
/// Register as a singleton.
/// </summary>
public sealed class MaxioSiteCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BillingSiteInfo? _value;
    private DateTimeOffset _expiresAt;

    public async Task<BillingSiteInfo> GetOrAddAsync(
        TimeSpan lifetime,
        Func<CancellationToken, Task<BillingSiteInfo>> factory,
        CancellationToken cancellationToken)
    {
        if (TryRead(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryRead(out cached))
            {
                return cached;
            }

            var fresh = await factory(cancellationToken).ConfigureAwait(false);

            if (lifetime > TimeSpan.Zero)
            {
                _value = fresh;
                _expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
            }

            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryRead(out BillingSiteInfo value)
    {
        var candidate = _value;
        if (candidate is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            value = candidate;
            return true;
        }

        value = default!;
        return false;
    }
}
