using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current recurring period, i.e. when the next scheduled charge occurs.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? Reference { get; set; }
}
