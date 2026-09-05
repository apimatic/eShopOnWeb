using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class CustomerSubscription
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
