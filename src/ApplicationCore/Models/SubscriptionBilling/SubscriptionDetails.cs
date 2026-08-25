using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// A shopper's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDetails
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
