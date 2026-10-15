using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Reads the plans offered by the configured Maxio product family (by handle, never by numeric id).</summary>
public class MaxioPlanCatalog
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioPlanCatalog> _logger;

    public MaxioPlanCatalog(MaxioAdvancedBillingClient client, MaxioSettings settings, ILogger<MaxioPlanCatalog> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Walks the family's product pages up to <see cref="MaxioBillingLimits.MaxPlanPages"/>; archived products are skipped.
    /// When the cap is reached with more pages possibly left, the result is marked <see cref="SubscriptionPlanCatalog.Truncated"/>.
    /// </summary>
    public async Task<SubscriptionPlanCatalog> ReadAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!.Trim();
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            if (page > MaxioBillingLimits.MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio product family {FamilyHandle} has more than {MaxProducts} products; the plan catalog is truncated.",
                    familyHandle, MaxioBillingLimits.MaxPlanPages * MaxioBillingLimits.PlanPageSize);
                break;
            }

            var batch = await ReadPageAsync(familyHandle, page, cancellationToken);
            foreach (var item in batch)
            {
                var product = item.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new SubscriptionPlan(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (batch.Count < MaxioBillingLimits.PlanPageSize)
            {
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    private async Task<IReadOnlyList<ProductResponse>> ReadPageAsync(string familyHandle, int page, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + familyHandle,
                    Page = page,
                    PerPage = MaxioBillingLimits.PlanPageSize
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                _logger.LogError("Maxio product family {FamilyHandle} was not found (HTTP {Status}); check Maxio:ProductFamilyHandle.",
                    familyHandle, (int)ex.StatusCode);
                throw new BillingProviderException(BillingFailureKind.Misconfigured,
                    "The subscription plan catalog is not available.", (int)ex.StatusCode, innerException: ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Listing Maxio plans failed with HTTP {Status}.", (int)raw.StatusCode);
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BillingProviderException(BillingFailureKind.Misconfigured,
                        "The subscription plan catalog is not available.", (int)raw.StatusCode, innerException: ex);
                }
                throw MaxioFailures.FromStatus(raw.StatusCode, ex);
            }
            throw MaxioFailures.FromStatus(ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Listing Maxio plans failed.");
            throw MaxioFailures.FromSdk(ex);
        }
    }
}
