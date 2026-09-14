using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A customer's subscription, as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
