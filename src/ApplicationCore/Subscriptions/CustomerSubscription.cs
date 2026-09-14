using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscriber's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public long Id { get; init; }

    /// <summary>Identifier assigned by this application; also used to make enrollment idempotent.</summary>
    public string? Reference { get; init; }

    public required string State { get; init; }

    /// <summary>False only once the subscription has reached a terminal state.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>When the next renewal will be assessed. Null once the subscription has ended.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public long BalanceInCents { get; init; }

    public decimal Balance => BalanceInCents / 100m;

    public string? PaymentCollectionMethod { get; init; }

    public long CustomerId { get; init; }

    public string? CustomerReference { get; init; }
}
