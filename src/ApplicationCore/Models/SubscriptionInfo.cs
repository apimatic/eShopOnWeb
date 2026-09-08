using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription as recorded by the billing system.
/// </summary>
public class SubscriptionInfo
{
    public int SubscriptionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string State { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
