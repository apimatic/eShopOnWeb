using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>A shopper's subscription as confirmed by Maxio, the billing system of record.</summary>
public class SubscriptionDetails
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
