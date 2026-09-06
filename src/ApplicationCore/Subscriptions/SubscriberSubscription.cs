using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record.
/// </summary>
public class SubscriberSubscription
{
    /// <summary>Billing-system identifier for the subscription.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state (for example "active", "trialing", "past_due", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Price actually being billed for this subscription, in the minor unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public string? Currency { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>When the next charge will be attempted. Null once the subscription reaches end of life.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? TrialEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>How the subscription is collected ("automatic", "remittance", "prepaid", "invoice").</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Outstanding balance in the minor unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>The eShopOnWeb-supplied reference stored against the subscription.</summary>
    public string? Reference { get; init; }

    /// <summary>Billing-system identifier of the customer that owns the subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>The eShopOnWeb-supplied reference stored against the customer.</summary>
    public string? CustomerReference { get; init; }
}
