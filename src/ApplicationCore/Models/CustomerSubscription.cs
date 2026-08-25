using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in the billing system (Maxio).
/// </summary>
public class CustomerSubscription
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public long CustomerId { get; set; }
}
