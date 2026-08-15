using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription.</summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Human-readable price, e.g. <c>$299.00/month</c>.</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Next billing/assessment date reported by the billing system.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public long CustomerId { get; set; }
}
