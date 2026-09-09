using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public long CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}
