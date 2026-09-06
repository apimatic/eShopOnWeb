using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it currently exists in the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public long Id { get; init; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>True while the subscription still entitles the shopper to the product.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long PlanPriceInCents { get; init; }
    public decimal PlanPrice => PlanPriceInCents / 100m;
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? PricePointName { get; init; }

    /// <summary>Date the next renewal charge is scheduled for (Maxio: next_assessment_at).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? TrialEndedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public long BalanceInCents { get; init; }
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The value this application supplied as the subscription's reference, when one was used.</summary>
    public string? Reference { get; init; }

    public long CustomerId { get; init; }
    public string? CustomerReference { get; init; }
    public string? CustomerEmail { get; init; }
}
