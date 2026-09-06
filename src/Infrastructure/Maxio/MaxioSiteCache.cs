using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Caches the configuration of the Maxio site the credentials point at.
/// </summary>
/// <remarks>
/// Site configuration decides how a subscription has to be created (see
/// <see cref="MaxioSite.RelationshipInvoicingEnabled"/>), so it cannot be assumed - but it changes
/// rarely, and re-reading it on every signup would waste part of the site's concurrency budget.
/// Registered as a singleton; the API client is passed per call so no scoped dependency is captured.
/// </remarks>
public sealed class MaxioSiteCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;

    private MaxioSite? _site;
    private DateTimeOffset _refreshAfter;

    public MaxioSiteCache() : this(TimeProvider.System)
    {
    }

    public MaxioSiteCache(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public async Task<MaxioSite> GetAsync(IMaxioApiClient client, CancellationToken cancellationToken)
    {
        var cached = _site;
        if (cached is not null && _timeProvider.GetUtcNow() < _refreshAfter)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_site is not null && _timeProvider.GetUtcNow() < _refreshAfter)
            {
                return _site;
            }

            var site = await client.ReadSiteAsync(cancellationToken);
            _site = site;
            _refreshAfter = _timeProvider.GetUtcNow().Add(CacheDuration);
            return site;
        }
        finally
        {
            _gate.Release();
        }
    }
}
