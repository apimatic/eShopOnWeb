using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A user's recurring subscription, hosted in Maxio Advanced Billing
/// (the billing system of record).
/// </summary>
public class UserSubscription
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
}
