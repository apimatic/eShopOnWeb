using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The plans this application offers: the non-archived products of the configured product family.
/// Shared by plan browsing and by the subscribe-time validation, so both agree on what is offered.
/// </summary>
public class MaxioPlanCatalog
{
    private const int PER_PAGE = 200; // the record's documented maximum
    private const int MAX_PAGES = 10;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private int? _familyId; // handle -> id, resolved once

    public MaxioPlanCatalog(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var familyId = await ResolveFamilyIdAsync(cancellationToken);
            var plans = new List<SubscriptionPlan>();
            var truncated = false;

            for (var page = 1; ; page++)
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId.ToString(),
                        Page = page,
                        PerPage = PER_PAGE
                    },
                    cancellationToken: cancellationToken);

                foreach (var item in products)
                {
                    var p = item.Product;
                    if (p is null || string.IsNullOrWhiteSpace(p.Handle) || p.ArchivedAt is not null)
                    {
                        continue;
                    }
                    plans.Add(new SubscriptionPlan(p.Handle, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value));
                }

                if (products.Count < PER_PAGE)
                {
                    break;
                }
                if (page >= MAX_PAGES)
                {
                    truncated = true;
                    break;
                }
            }

            return new SubscriptionPlanList(plans, truncated);
        }
        catch (Exception ex) when (ex is SdkException or SubscriptionException)
        {
            throw MaxioErrors.Translate(ex, "list subscription plans");
        }
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_familyId is int cached)
        {
            return cached;
        }

        var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: cancellationToken);
        foreach (var item in families)
        {
            var f = item.ProductFamily;
            if (f?.Id is int id && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
            {
                _familyId = id;
                return id;
            }
        }

        throw new SubscriptionException(502, "The configured product family was not found in billing.");
    }
}
