using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrolment in a <see cref="SubscriptionPlan"/>, as reported by the billing provider,
/// which is the system of record for it.
/// </summary>
public record Subscription
{
    /// <summary>Maxio's subscription id.</summary>
    public required int Id { get; init; }

    /// <summary>
    /// The reference this application assigned to the subscription. It is unique per site and is what
    /// makes subscribe idempotent.
    /// </summary>
    public string? Reference { get; init; }

    public required SubscriptionState State { get; init; }

    /// <summary>The provider's raw state string, preserved for diagnostics and forward compatibility.</summary>
    public required string RawState { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>Price actually being charged for this subscription, in minor units.</summary>
    public required long PriceInCents { get; init; }

    public required string Currency { get; init; }

    public required BillingInterval Interval { get; init; }

    /// <summary>
    /// When payment will next be captured. Tracks the end of the current period unless a renewal
    /// payment failed and is being retried.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? TrialEndsAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Outstanding balance on the subscription, in minor units.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How Maxio collects payment for this subscription (e.g. <c>automatic</c>, <c>remittance</c>).</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Maxio's customer id backing this subscription.</summary>
    public required int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public bool IsLive => State.IsLive();
}
