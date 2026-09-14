using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a shopper, as reported by Maxio (the billing system of record).
/// </summary>
public class SubscriberSubscription
{
    public int SubscriptionId { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }

    public string Currency { get; set; } = "USD";

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public int CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}
