using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription as recorded in Maxio.</summary>
public class CustomerSubscriptionDto
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Maxio subscription state (e.g. "active", "trialing"), or "unknown" when absent.</summary>
    public string State { get; set; } = "unknown";

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }

    /// <summary>The next date Maxio will bill this subscription.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
