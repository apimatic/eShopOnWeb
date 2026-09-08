using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as recorded by Maxio Advanced Billing (the billing system of record).
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string State { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public long? PlanId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PlanInterval { get; set; } = 1;

    public string PlanIntervalUnit { get; set; } = "month";

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>The date the next billing assessment is scheduled for.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
