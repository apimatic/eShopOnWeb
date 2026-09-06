using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Caches the Maxio site record for the process. Site settings (billing currency, billing
/// architecture) change rarely but are needed on nearly every call, so they are fetched once and
/// refreshed on a slow timer rather than on every request.
/// </summary>
public class MaxioSiteCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private MaxioSite? _site;
    private DateTimeOffset _refreshAfter = DateTimeOffset.MinValue;

    public async Task<MaxioSite> GetAsync(
        Func<CancellationToken, Task<MaxioSite>> factory,
        CancellationToken cancellationToken = default)
    {
        if (_site is not null && DateTimeOffset.UtcNow < _refreshAfter)
        {
            return _site;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_site is not null && DateTimeOffset.UtcNow < _refreshAfter)
            {
                return _site;
            }

            _site = await factory(cancellationToken);
            _refreshAfter = DateTimeOffset.UtcNow.Add(Lifetime);
            return _site;
        }
        finally
        {
            _gate.Release();
        }
    }
}
