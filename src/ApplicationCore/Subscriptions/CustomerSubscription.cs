using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as the billing system of record sees it.
/// </summary>
public sealed record CustomerSubscription
{
    public required int Id { get; init; }

    /// <summary>Our own reference for the subscription, when one was supplied at signup.</summary>
    public string? Reference { get; init; }

    /// <summary>Provider lifecycle state, e.g. active, trialing, past_due, canceled.</summary>
    public required string State { get; init; }

    /// <summary>True when the subscription currently entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>Recurring price of the subscribed plan version, in minor units.</summary>
    public required long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public required string Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the next charge will be attempted. Tracks the end of the current period, but diverges
    /// while a failed payment is being retried, which is why it is reported separately.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Outstanding balance in minor units.</summary>
    public long BalanceInCents { get; init; }

    public string? PaymentCollectionMethod { get; init; }

    public int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public string? CustomerEmail { get; init; }
}
