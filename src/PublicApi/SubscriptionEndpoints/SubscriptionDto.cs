using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription belonging to the authenticated user, backed by Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
