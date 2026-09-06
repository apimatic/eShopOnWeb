using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's recurring subscription, as held by the billing provider.
/// <para>
/// Projected from the Maxio <c>Subscription</c> schema (<c>components/schemas/Subscription.yaml</c>).
/// </para>
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>Maxio <c>subscription.state</c>, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The reference value this application assigned to the subscription (Maxio <c>subscription.reference</c>).</summary>
    public string? Reference { get; init; }

    /// <summary>Handle of the subscribed plan (Maxio <c>subscription.product.handle</c>).</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Name of the subscribed plan (Maxio <c>subscription.product.name</c>).</summary>
    public string? PlanName { get; init; }

    /// <summary>Maxio <c>subscription.product.id</c>.</summary>
    public int? PlanId { get; init; }

    /// <summary>Recurring amount currently charged, in minor units (Maxio <c>subscription.product_price_in_cents</c>).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring amount as a decimal, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Maxio <c>subscription.currency</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// When the provider will next attempt to bill (Maxio <c>subscription.next_assessment_at</c>).
    /// Usually tracks <see cref="CurrentPeriodEndsAt"/>, but diverges while a failed payment is being retried.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>Maxio <c>subscription.current_period_started_at</c>.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>Maxio <c>subscription.current_period_ends_at</c>.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>Maxio <c>subscription.activated_at</c>.</summary>
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>Maxio <c>subscription.canceled_at</c>.</summary>
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>Maxio <c>subscription.trial_started_at</c>.</summary>
    public DateTimeOffset? TrialStartedAt { get; init; }

    /// <summary>Maxio <c>subscription.trial_ended_at</c>.</summary>
    public DateTimeOffset? TrialEndedAt { get; init; }

    /// <summary>Maxio <c>subscription.created_at</c>.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Maxio <c>subscription.balance_in_cents</c>.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>Maxio <c>subscription.total_revenue_in_cents</c>.</summary>
    public long TotalRevenueInCents { get; init; }

    /// <summary>Maxio <c>subscription.payment_collection_method</c>.</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The customer the subscription belongs to (Maxio <c>subscription.customer</c>).</summary>
    public BillingCustomer? Customer { get; init; }

    /// <summary>
    /// True while the provider still considers the subscription a going concern. Terminal states
    /// (<c>canceled</c>, <c>expired</c>, <c>failed_to_create</c>, <c>trial_ended</c>) are not live, and
    /// therefore never block a fresh signup to the same plan.
    /// </summary>
    public bool IsLive => !SubscriptionStates.IsTerminal(State);
}
