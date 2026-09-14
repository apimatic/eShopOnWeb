using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, projected from a Maxio
/// <c>Subscription</c> (see <c>Subscription.yaml</c> in the Maxio OpenAPI specification).
/// </summary>
public sealed class CustomerSubscription
{
    public required long Id { get; init; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>The reference eShopOnWeb assigned at signup; also the idempotency token.</summary>
    public string? Reference { get; init; }

    public required long CustomerId { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price actually charged for this subscription, in minor units.</summary>
    public long PriceInCents { get; init; }

    public string? Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>
    /// Maxio <c>next_assessment_at</c>: when payment will next be attempted. Null once the
    /// subscription reaches an end-of-life state.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? TrialEndedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public long BalanceInCents { get; init; }

    /// <summary>Maxio <c>payment_collection_method</c>, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>True while the subscription still occupies the plan for this shopper.</summary>
    public bool IsLive => !SubscriptionStates.IsEndOfLife(State);
}
