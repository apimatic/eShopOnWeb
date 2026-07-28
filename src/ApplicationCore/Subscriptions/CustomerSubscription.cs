using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, projected from the billing
/// system of record. This is the confirmation returned after subscribing and the shape
/// listed under a customer's active subscriptions.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Billing-system identifier for the subscription.</summary>
    public long Id { get; init; }

    /// <summary>Current lifecycle state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the plan the shopper is subscribed to.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Display name of the plan.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>The recurring price formatted for display (e.g. "$299.00").</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>When the current billing period ends — i.e. the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>Stable customer reference (the eShopOnWeb user identity).</summary>
    public string CustomerReference { get; init; } = string.Empty;
}
