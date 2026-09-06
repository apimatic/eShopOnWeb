using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper enrollment in a <see cref="SubscriptionPlan"/>, as held by the billing system of record.
/// </summary>
public record CustomerSubscription
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public required long Id { get; init; }

    /// <summary>The reference this application assigned to the subscription; the idempotency key of the enrollment.</summary>
    public string? Reference { get; init; }

    /// <summary>Lifecycle state reported by the billing system, verbatim (for example <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public required string State { get; init; }

    /// <summary>Handle of the subscribed plan. Null for subscriptions that are not backed by a catalog product.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Display name of the subscribed plan.</summary>
    public string? PlanName { get; init; }

    /// <summary>Price of one billing period, in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code the subscription is billed in.</summary>
    public string? Currency { get; init; }

    /// <summary>Length of a billing period, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int? Interval { get; init; }

    /// <summary>Unit of the billing period, for example <c>month</c>.</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Outstanding balance, in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How the billing system collects payment, for example <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Identifier of the billing-system customer that owns this subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>Start of the billing period currently in progress.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>End of the billing period currently in progress.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription is next assessed for billing: the shopper-facing next billing date.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the subscription became active.</summary>
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>When the trial period ended, if the plan had one.</summary>
    public DateTimeOffset? TrialEndedAt { get; init; }

    /// <summary>When the subscription was canceled, if it was.</summary>
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>When the subscription expires, if it has a fixed end.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>When the subscription record was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Price of one billing period as a major-unit amount (for example 299.00).</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);
}
