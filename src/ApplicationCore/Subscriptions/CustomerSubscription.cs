using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a subscription plan, as confirmed by the billing system.
/// Prices are in integer minor units (cents).
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int subscriptionId,
        string? planHandle,
        string? planName,
        int? productId,
        long? priceInCents,
        string state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt)
    {
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        ProductId = productId;
        PriceInCents = priceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
    }

    /// <summary>Numeric subscription id in the billing system.</summary>
    public int SubscriptionId { get; }

    /// <summary>Handle of the plan/product the subscription is bound to.</summary>
    public string? PlanHandle { get; }

    /// <summary>Name of the plan/product.</summary>
    public string? PlanName { get; }

    /// <summary>Numeric product id.</summary>
    public int? ProductId { get; }

    /// <summary>Recurring price in integer minor units (cents).</summary>
    public long? PriceInCents { get; }

    /// <summary>Subscription lifecycle state wire value (e.g. "active", "pending", "canceled").</summary>
    public string State { get; }

    /// <summary>
    /// End of the current billing period. The billing system does not echo a distinct
    /// "next billing" field on the subscription payload, so this doubles as the next
    /// billing date for the shopper.
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the subscription is next assessed for billing, when supplied.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }
}
