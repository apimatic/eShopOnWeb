using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Services;

/// <summary>
/// Serves the subscription plan catalog from the Maxio product family named in configuration.
/// </summary>
/// <remarks>
/// The family is addressed by handle rather than by id, using the <c>handle:</c> prefix the
/// specification defines for the <c>product_family_id</c> path parameter. Numeric ids change when a
/// catalog is re-seeded; handles do not.
/// </remarks>
public class MaxioSubscriptionPlanCatalog : ISubscriptionPlanCatalog
{
    private const string PlansCacheKey = "maxio:plans";
    private const string SiteCacheKey = "maxio:site-currency";

    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionPlanCatalog> _logger;

    public MaxioSubscriptionPlanCatalog(
        IMaxioApiClient client,
        IMemoryCache cache,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioSubscriptionPlanCatalog> logger)
    {
        _client = client;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = GetValidatedOptions();

        if (_cache.TryGetValue(PlansCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            // A second caller may have filled the cache while this one waited for the gate.
            if (_cache.TryGetValue(PlansCacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var currency = await GetSiteCurrencyAsync(options, cancellationToken);
            var familyPath = $"handle:{options.ProductFamilyHandle!.Trim()}";

            var products = await _client.ListProductsForProductFamilyAsync(familyPath, cancellationToken);

            var plans = products
                .Where(MaxioMapper.IsSubscribable)
                .Select(product => MaxioMapper.ToPlan(product, currency))
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation(
                "Loaded {PlanCount} subscription plan(s) from Maxio product family '{ProductFamilyHandle}'.",
                plans.Count, options.ProductFamilyHandle);

            _cache.Set(PlansCacheKey, (IReadOnlyList<SubscriptionPlan>)plans, CacheLifetime(options));
            return plans;
        }
        catch (BillingProviderException ex) when (ex.ProviderStatusCode == 404)
        {
            throw new BillingConfigurationException(
                $"Maxio has no product family with handle '{options.ProductFamilyHandle}'. "
                + $"Check '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}'.");
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> GetSiteCurrencyAsync(MaxioOptions options, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out string? currency) && !string.IsNullOrWhiteSpace(currency))
        {
            return currency;
        }

        var site = await _client.ReadSiteAsync(cancellationToken);
        currency = string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency;

        _cache.Set(SiteCacheKey, currency, CacheLifetime(options));
        return currency;
    }

    private static TimeSpan CacheLifetime(MaxioOptions options) =>
        TimeSpan.FromSeconds(Math.Max(1, options.CatalogCacheSeconds));

    private MaxioOptions GetValidatedOptions()
    {
        var options = _options.CurrentValue;
        var problems = options.Validate().ToList();

        if (problems.Count > 0)
        {
            throw new BillingConfigurationException(
                "Subscription billing is not configured: " + string.Join(" ", problems));
        }

        return options;
    }
}
