using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as held by the billing provider.
/// </summary>
public record Subscription
{
    /// <summary>Provider-assigned identifier of the subscription.</summary>
    public required long Id { get; init; }

    /// <summary>
    /// Application-supplied reference. It is unique per site at the provider, which is what makes
    /// enrollment idempotent.
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>Provider lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public required string State { get; init; }

    /// <summary>True while the subscription still entitles the customer to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>Price actually charged for this subscription, in the minor unit of <see cref="Currency"/>.</summary>
    public required int PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public required string Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>How the provider collects payment, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; init; }

    public required long CustomerId { get; init; }

    public string? CustomerReference { get; init; }
}
