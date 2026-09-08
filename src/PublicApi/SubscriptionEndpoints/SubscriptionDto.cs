using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as reported by Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
