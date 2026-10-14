using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A plan a shopper can subscribe to: a non-archived product of the configured Maxio product family.</summary>
public record SubscriptionPlan(
    int? ProductId,
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// The plans on offer. <see cref="Truncated"/> is true when the page cap stopped the read before Maxio signalled
/// the end, so <see cref="Plans"/> may be incomplete.
/// </summary>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated)
{
    public SubscriptionPlan? Find(string handle) =>
        Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.Ordinal));
}

public interface ISubscriptionPlanCatalog
{
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken);
}

public class MaxioSubscriptionPlanCatalog : ISubscriptionPlanCatalog
{
    // Maxio's documented maximum page size for this list.
    internal const int PageSize = 200;
    internal const int MaxPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionPlanCatalog> _logger;

    public MaxioSubscriptionPlanCatalog(MaxioAdvancedBillingClient client, IMemoryCache cache, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionPlanCatalog> logger)
    {
        _client = client;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";
        if (_options.PlanCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await LoadAsync(cancellationToken);

        if (_options.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, catalog, TimeSpan.FromSeconds(_options.PlanCacheSeconds));
        }

        return catalog;
    }

    private async Task<SubscriptionPlanCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            var products = await ListPageAsync(page, cancellationToken);

            foreach (var item in products)
            {
                var product = item.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }

                if (product.RequireCreditCard == true)
                {
                    // This flow collects no payment method, so such a product cannot be subscribed here.
                    _logger.LogInformation("Maxio product {ProductHandle} requires a payment method and is not offered as a plan.", product.Handle);
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Id,
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (products.Count < PageSize)
            {
                break;
            }

            if (page >= MaxPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio plan list for family {ProductFamilyHandle} stopped at the {MaxPages}-page cap; the result is marked truncated.", _options.ProductFamilyHandle, MaxPages);
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    private async Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListPageAsync(int page, CancellationToken cancellationToken)
    {
        const string operation = "ListProductsForProductFamily";
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    // The operation accepts the family's id or its handle prefixed with "handle:".
                    ProductFamilyId = "handle:" + _options.ProductFamilyHandle,
                    Page = page,
                    PerPage = PageSize
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                // 404: the configured family does not exist on this site — a deployment fault.
                _logger.LogError("Maxio product family {ProductFamilyHandle} was not found ({StatusCode}): {ProviderMessage}. Check Maxio:ProductFamilyHandle.", _options.ProductFamilyHandle, (int)ex.StatusCode, notFound);
                throw new BillingException(StatusCodes.Status503ServiceUnavailable, "Subscription plans are not available right now.", null, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MaxioFailures.FromStatus(_logger, operation, raw.StatusCode, null, ex);
            }

            throw MaxioFailures.FromStatus(_logger, operation, ex.StatusCode, null, ex);
        }
        catch (SdkException ex)
        {
            throw MaxioFailures.FromSdk(_logger, operation, ex);
        }
    }
}
