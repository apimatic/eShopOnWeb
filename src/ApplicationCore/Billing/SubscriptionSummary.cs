using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A confirmed view of a subscription as it exists in Maxio, surfaced back to the shopper.
/// </summary>
public class SubscriptionSummary
{
    /// <summary>The Maxio subscription id.</summary>
    public int SubscriptionId { get; init; }

    /// <summary>The Maxio customer id owning this subscription.</summary>
    public int CustomerId { get; init; }

    /// <summary>Subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price of the subscribed product in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>
    /// When the current period ends and the next regularly-scheduled charge occurs
    /// (the "next billing date").
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>When the current billing period started.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>When the subscription was created in Maxio.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
