using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
