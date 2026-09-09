using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription the authenticated shopper holds in Maxio Advanced Billing.
/// </summary>
public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public string State { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public int? PriceInCents { get; set; }

    public string? Currency { get; set; }

    public DateTime? NextBillingDateUtc { get; set; }

    public DateTime? CreatedAtUtc { get; set; }

    public DateTime? CanceledAtUtc { get; set; }
}
