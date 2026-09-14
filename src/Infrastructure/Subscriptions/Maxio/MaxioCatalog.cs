using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Read-through cache over the parts of the Maxio catalog that change rarely: the site's currency and
/// billing architecture, the configured product family, and the products in it that shoppers may
/// subscribe to. Everything is keyed by site and family handle so a configuration change cannot serve
/// stale entries from a previous target.
/// </summary>
public class MaxioCatalog
{
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioCatalog> _logger;

    public MaxioCatalog(IMemoryCache cache, IOptionsMonitor<MaxioOptions> options, ILogger<MaxioCatalog> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <summary>Site-level facts, cached.</summary>
    public async Task<MaxioSite> GetSiteAsync(AdvancedBillingClient client, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var key = $"maxio:site:{options.Subdomain}";

        if (_cache.TryGetValue<MaxioSite>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        SiteResponse response;

        try
        {
            response = await client.SitesController.ReadSiteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException exception)
        {
            throw MaxioErrorTranslator.Translate(exception, "read the Maxio site configuration");
        }

        var site = new MaxioSite(
            string.IsNullOrWhiteSpace(response.Site?.Currency) ? MaxioSite.DefaultCurrency : response.Site!.Currency!,
            response.Site?.RelationshipInvoicingEnabled ?? false);

        _cache.Set(key, site, options.CatalogCacheDuration);
        return site;
    }

    /// <summary>
    /// The plans on offer, cheapest first. Pass <paramref name="refresh"/> to bypass the cache after a
    /// miss that the caller expects to be a stale-catalog problem.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(
        AdvancedBillingClient client,
        CancellationToken cancellationToken,
        bool refresh = false)
    {
        var options = _options.CurrentValue;
        var key = $"maxio:plans:{options.Subdomain}:{options.ProductFamilyHandle}";

        if (!refresh && _cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var site = await GetSiteAsync(client, cancellationToken).ConfigureAwait(false);
        var family = await GetProductFamilyAsync(client, cancellationToken, refresh).ConfigureAwait(false);

        List<ProductResponse> products;

        try
        {
            products = await client.ProductFamiliesController.ListProductsForProductFamilyAsync(
                new ListProductsForProductFamilyInput
                {
                    ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
                    PerPage = 200,
                    IncludeArchived = false,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException exception)
        {
            throw MaxioErrorTranslator.Translate(exception, $"list the plans in product family '{options.ProductFamilyHandle}'");
        }

        var plans = products
            .Select(product => product.Product)
            .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MaxioMapper.ToPlan(product!, site.Currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogDebug("Loaded {PlanCount} Maxio plan(s) from product family {ProductFamilyHandle}.",
            plans.Length, options.ProductFamilyHandle);

        _cache.Set<IReadOnlyList<SubscriptionPlan>>(key, plans, options.CatalogCacheDuration);
        return plans;
    }

    /// <summary>
    /// Finds a plan by handle. A miss is retried once against a freshly loaded catalog, so a plan added
    /// to Maxio moments ago is still subscribable rather than being rejected for the cache's lifetime.
    /// </summary>
    public async Task<SubscriptionPlan?> FindPlanAsync(
        AdvancedBillingClient client,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(client, cancellationToken).ConfigureAwait(false);
        var plan = Find(plans, planHandle);

        if (plan is not null)
        {
            return plan;
        }

        plans = await GetPlansAsync(client, cancellationToken, refresh: true).ConfigureAwait(false);
        return Find(plans, planHandle);
    }

    private static SubscriptionPlan? Find(IReadOnlyList<SubscriptionPlan> plans, string planHandle) =>
        plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the configured product family handle to the numeric id the products endpoint needs.
    /// Handles are stable across Maxio re-seeds; the ids they resolve to are not, which is why the id is
    /// never configured and only ever cached for a short while.
    /// </summary>
    private async Task<ProductFamily> GetProductFamilyAsync(
        AdvancedBillingClient client,
        CancellationToken cancellationToken,
        bool refresh)
    {
        var options = _options.CurrentValue;
        var key = $"maxio:family:{options.Subdomain}:{options.ProductFamilyHandle}";

        if (!refresh && _cache.TryGetValue<ProductFamily>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        List<ProductFamilyResponse> families;

        try
        {
            families = await client.ProductFamiliesController
                .ListProductFamiliesAsync(new ListProductFamiliesInput(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ApiException exception)
        {
            throw MaxioErrorTranslator.Translate(exception, "list the Maxio product families");
        }

        var family = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(candidate => candidate?.Id is not null &&
                string.Equals(candidate.Handle, options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new SubscriptionBillingNotConfiguredException(
                $"Maxio site '{options.Subdomain}' has no product family with handle '{options.ProductFamilyHandle}'. " +
                $"Set {MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} to one of: " +
                string.Join(", ", families.Select(f => f.ProductFamily?.Handle).Where(h => !string.IsNullOrWhiteSpace(h))));
        }

        _cache.Set(key, family, options.CatalogCacheDuration);
        return family;
    }
}
