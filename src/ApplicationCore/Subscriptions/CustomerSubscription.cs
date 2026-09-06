using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as held by the billing system.
/// </summary>
public sealed record CustomerSubscription
{
    public required int Id { get; init; }

    /// <summary>The reference this application assigned to the subscription at signup, when present.</summary>
    public string? Reference { get; init; }

    /// <summary>Billing-system subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public required string State { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>The recurring amount for this subscription, in the smallest unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    public required string Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next charge will be assessed. Tracks the period end unless a payment is being retried.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? TrialEndedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Outstanding balance in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How the billing system collects payment, e.g. <c>remittance</c> or <c>automatic</c>.</summary>
    public string? PaymentCollectionMethod { get; init; }

    public required int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
