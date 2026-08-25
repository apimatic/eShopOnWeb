using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded in the billing system of record.
/// </summary>
public class ShopperSubscription
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public decimal ProductPrice => ProductPriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
