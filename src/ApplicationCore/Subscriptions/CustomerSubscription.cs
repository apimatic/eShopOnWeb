using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>. Provider-neutral projection of a
/// Maxio Advanced Billing subscription.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Provider identifier of the subscription.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state as reported by the billing system, e.g. "active", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the subscribed plan, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Display name of the subscribed plan.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (PriceInCents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>
    /// The date the next regularly scheduled charge is due — i.e. the next billing date.
    /// Maps to Maxio's <c>current_period_ends_at</c>.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>
    /// When the billing system will next attempt to capture payment. Usually equal to
    /// <see cref="NextBillingDate"/>, but can diverge during dunning. Maps to Maxio's <c>next_assessment_at</c>.
    /// </summary>
    public DateTimeOffset? NextAssessmentDate { get; init; }

    /// <summary>Provider identifier of the customer that owns this subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>External reference used to tie the billing customer back to the eShopOnWeb user.</summary>
    public string CustomerReference { get; init; } = string.Empty;

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
