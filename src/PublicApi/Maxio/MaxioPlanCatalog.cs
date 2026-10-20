using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The plans the shop offers: the non-archived products of family <c>Maxio:ProductFamilyHandle</c>.
/// This list is also the legal set for <c>POST /api/subscriptions</c>.
/// </summary>
public class MaxioPlanCatalog
{
    /// <summary>The request record's maximum page size.</summary>
    public const int PageSize = 200;

    /// <summary>Page cap: the walk never depends on the provider alone to stop.</summary>
    public const int MaxPages = 5;

    private static readonly TimeSpan FamilyIdCacheDuration = TimeSpan.FromMinutes(30);

    private readonly MaxioGateway _gateway;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioPlanCatalog> _logger;

    public MaxioPlanCatalog(MaxioGateway gateway, IMemoryCache cache, IOptions<MaxioOptions> options, ILogger<MaxioPlanCatalog> logger)
    {
        _gateway = gateway;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task<PlanCatalog> GetPlansAsync(CancellationToken ct)
    {
        var familyHandle = _options.ProductFamilyHandle!;
        var cacheKey = "maxio:product-family-id:" + familyHandle;

        if (_cache.TryGetValue(cacheKey, out string? cachedId) && cachedId is not null)
        {
            var cached = await WalkAsync(cachedId, ct);
            if (cached is not null)
            {
                return cached;
            }
            _cache.Remove(cacheKey);
        }

        // The documented id form: the family handle prefixed with "handle:".
        var byHandle = await WalkAsync("handle:" + familyHandle, ct);
        if (byHandle is not null)
        {
            return byHandle;
        }

        // Fallback: resolve the numeric id from the family list, matching the handle exactly.
        _logger.LogWarning("Maxio did not find the product family by its handle form; resolving its id from the family list.");
        var families = await _gateway.ListProductFamiliesAsync(ct);
        var family = families.FirstOrDefault(f => string.Equals(f.Handle, familyHandle, StringComparison.Ordinal) && f.Id is not null);
        if (family is null)
        {
            _logger.LogError("The configured Maxio product family (Maxio:ProductFamilyHandle) does not exist on the site.");
            throw new MaxioBillingException(HttpStatus.BadGateway, "The subscription plan catalog is not available.");
        }

        var familyId = family.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var byId = await WalkAsync(familyId, ct);
        if (byId is null)
        {
            throw new MaxioBillingException(HttpStatus.BadGateway, "The subscription plan catalog is not available.");
        }
        _cache.Set(cacheKey, familyId, FamilyIdCacheDuration);
        return byId;
    }

    /// <summary>Walks the pages; <c>null</c> when the family id is not found.</summary>
    private async Task<PlanCatalog?> WalkAsync(string productFamilyId, CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await _gateway.ListProductsPageAsync(productFamilyId, page, PageSize, ct);
            if (result is null)
            {
                if (page == 1)
                {
                    return null;
                }
                throw new MaxioBillingException(HttpStatus.BadGateway, "The subscription plan catalog is not available.");
            }

            plans.AddRange(result.Offered);
            if (result.RawCount < PageSize)
            {
                return new PlanCatalog(plans, Truncated: false);
            }
        }

        _logger.LogWarning("Maxio plan list hit the page cap ({MaxPages} pages of {PageSize}); returning a truncated catalog.", MaxPages, PageSize);
        return new PlanCatalog(plans, Truncated: true);
    }
}
