using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// A subscription of a shopper as reported by the billing system of record. Projected from the
/// Maxio <c>Subscription</c> schema.
/// </summary>
public sealed record SubscriberSubscription
{
    /// <summary>The provider-assigned subscription id.</summary>
    public required long Id { get; init; }

    /// <summary>Our own reference for the subscription; doubles as the idempotency marker.</summary>
    public string? Reference { get; init; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>True when the state is one that still represents an existing enrollment.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    /// <summary>Handle of the plan the subscription is enrolled on.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Display name of the plan the subscription is enrolled on.</summary>
    public string? PlanName { get; init; }

    /// <summary>The recurring amount for the subscribed plan version, in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>The recurring amount expressed in major units.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit &#8212; "day" or "month".</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>ISO currency code of the subscription, when the provider reports one.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// When payment will next be attempted; the <c>next_assessment_at</c> field of the provider.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>Start of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription went live (came out of trial, or began without one).</summary>
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>When the trial period, if any, ended.</summary>
    public DateTimeOffset? TrialEndedAt { get; init; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>When the subscription was most recently canceled.</summary>
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>Outstanding balance on the subscription, in cents.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How payment is collected, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The provider-side customer id owning this subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>Our reference on the provider-side customer record.</summary>
    public string? CustomerReference { get; init; }
}
