using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription, as reported by the billing system.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public int CustomerId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
