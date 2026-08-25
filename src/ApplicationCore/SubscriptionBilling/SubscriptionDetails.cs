using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A shopper's subscription as recorded in Maxio, the billing system of record.
/// </summary>
public class SubscriptionDetails
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>When Maxio will next bill/assess the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
