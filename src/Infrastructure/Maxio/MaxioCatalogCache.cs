using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads the parts of the Maxio catalogue that change rarely - the plan list and the site's currency.
/// </summary>
public interface IMaxioCatalogCache
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<string> GetCurrencyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Caches the plan catalogue for a short, configurable window. Plans are edited by hand in Maxio and
/// are read on every page view, so serving them from cache keeps the storefront well clear of the
/// API's rate limits without hiding catalogue edits for long.
/// </summary>
public class MaxioCatalogCache : IMaxioCatalogCache
{
    private const string PlansCacheKey = "maxio:plans";
    private const string CurrencyCacheKey = "maxio:site-currency";

    /// <summary>Site currency effectively never changes, so it is held far longer than the plan list.</summary>
    private static readonly TimeSpan CurrencyCacheDuration = TimeSpan.FromMinutes(30);

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MaxioSettings> _settings;

    public MaxioCatalogCache(IMaxioApiClient client, IMemoryCache cache, IOptionsMonitor<MaxioSettings> settings)
    {
        _client = client;
        _cache = cache;
        _settings = settings;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;
        var cacheKey = $"{PlansCacheKey}:{settings.ProductFamilyHandle}";

        if (settings.CatalogCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var currency = await GetCurrencyAsync(cancellationToken).ConfigureAwait(false);

        // The path parameter accepts "either the product family's id or its handle prefixed with
        // handle:" - handles are the stable identifier, numeric ids are reassigned on reseed.
        var products = await _client
            .ListProductsForProductFamilyAsync($"handle:{settings.ProductFamilyHandle}", includeArchived: false, cancellationToken)
            .ConfigureAwait(false);

        var plans = products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => MaxioMapper.ToPlan(product, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.CatalogCacheSeconds > 0)
        {
            _cache.Set<IReadOnlyList<SubscriptionPlan>>(cacheKey, plans, TimeSpan.FromSeconds(settings.CatalogCacheSeconds));
        }

        return plans;
    }

    /// <summary>
    /// The site's primary currency, read from <c>GET /site.json</c>. Returns an empty string if the
    /// site does not report one - no currency is better than a guessed one - and does not cache that,
    /// so a later read can still pick it up.
    /// </summary>
    public async Task<string> GetCurrencyAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CurrencyCacheKey}:{_settings.CurrentValue.Subdomain}|{_settings.CurrentValue.BaseUrl}";

        if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        var site = await _client.ReadSiteAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(site.Currency))
        {
            return string.Empty;
        }

        _cache.Set(cacheKey, site.Currency!, CurrencyCacheDuration);
        return site.Currency!;
    }
}
