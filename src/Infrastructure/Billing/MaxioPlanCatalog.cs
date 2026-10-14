using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The plans this store offers: the non-archived products of the configured Maxio product family.
/// This list is also the legal set for the plan a shopper may subscribe to.
/// </summary>
public class MaxioPlanCatalog
{
    internal const int PlansPerPage = 200;   // Maxio clamps per_page to 200
    internal const int MaxPlanPages = 5;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioPlanCatalog> _logger;

    public MaxioPlanCatalog(MaxioAdvancedBillingClient client, IMemoryCache cache, IOptions<MaxioSettings> settings,
        ILogger<MaxioPlanCatalog> logger)
    {
        _client = client;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    private string FamilyHandle => _settings.ProductFamilyHandle!;

    public async Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{FamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanList? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await FetchPlansAsync(cancellationToken);
        _cache.Set(cacheKey, plans, CacheDuration);
        return plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(cancellationToken);
        return plans.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks the family's product pages. The list endpoint is not paginated by the SDK, so the loop drives
    /// page/per_page itself and stops on a short page or at <see cref="MaxPlanPages"/>; hitting the cap is
    /// reported through <see cref="SubscriptionPlanList.IsTruncated"/>.
    /// </summary>
    internal async Task<SubscriptionPlanList> FetchPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        var reachedEnd = false;

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = $"handle:{FamilyHandle}",
                        Page = page,
                        PerPage = PlansPerPage,
                        IncludeArchived = false
                    },
                    cancellationToken: cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    // 404: the configured family does not exist on this site — a deployment fault, not the caller's.
                    _logger.LogError(ex, "Maxio product family {ProductFamilyHandle} was not found (HTTP {Status}): {Detail}",
                        FamilyHandle, (int)ex.StatusCode, notFound);
                    throw new BillingProviderException(502, "Subscription plans are not available right now.", (int)ex.StatusCode, ex);
                }
                if (ex.Error.TryGetRawError(out RawError raw))
                {
                    _logger.LogError(ex, "Maxio rejected listing products of family {ProductFamilyHandle} (HTTP {Status})",
                        FamilyHandle, (int)raw.StatusCode);
                }
                throw MaxioErrorTranslator.FromStatus(ex.StatusCode, ex);
            }
            catch (SdkException ex)
            {
                _logger.LogError(ex, "Listing Maxio products of family {ProductFamilyHandle} failed", FamilyHandle);
                throw MaxioErrorTranslator.Translate(ex);
            }

            foreach (var item in products)
            {
                var plan = ToPlan(item.Product);
                if (plan is not null)
                {
                    plans.Add(plan);
                }
            }

            if (products.Count < PlansPerPage)
            {
                reachedEnd = true;
                break;
            }
        }

        if (!reachedEnd)
        {
            _logger.LogWarning("Plan list for family {ProductFamilyHandle} stopped at the {MaxPages}-page cap; result is truncated",
                FamilyHandle, MaxPlanPages);
        }

        return new SubscriptionPlanList(plans, IsTruncated: !reachedEnd);
    }

    private static SubscriptionPlan? ToPlan(Product? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
        {
            return null;
        }

        return new SubscriptionPlan(
            Handle: product.Handle,
            Name: product.Name ?? product.Handle,
            Description: product.Description,
            PriceInCents: product.PriceInCents,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value,
            InitialChargeInCents: product.InitialChargeInCents,
            TrialPriceInCents: product.TrialPriceInCents,
            TrialInterval: product.TrialInterval,
            TrialIntervalUnit: product.TrialIntervalUnit?.Value);
    }
}
