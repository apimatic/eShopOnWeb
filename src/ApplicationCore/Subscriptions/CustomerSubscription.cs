using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system.
/// </summary>
public sealed record CustomerSubscription
{
    public required int Id { get; init; }

    /// <summary>Billing-system subscription state, e.g. "active", "trialing", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>True while the subscription still occupies the shopper's slot for its plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The recurring amount actually being charged for this subscription, in cents.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public string? Currency { get; init; }

    /// <summary>
    /// When the next charge is scheduled. Tracks the end of the current period unless a payment
    /// failed and is being retried.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? TrialStartedAt { get; init; }

    public DateTimeOffset? TrialEndedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public bool CancelAtEndOfPeriod { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public long BalanceInCents { get; init; }

    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The reference this application supplied when creating the subscription, if any.</summary>
    public string? Reference { get; init; }

    public int? CustomerId { get; init; }

    public string? CustomerReference { get; init; }
}
