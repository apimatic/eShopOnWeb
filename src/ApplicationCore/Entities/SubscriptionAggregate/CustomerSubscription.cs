using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An enrollment of a shopper in a <see cref="SubscriptionPlan"/>, as reported by Maxio.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>Maxio subscription state, e.g. "active". See <see cref="SubscriptionStates"/>.</summary>
    public required string State { get; init; }

    public int CustomerId { get; init; }

    /// <summary>Our idempotency marker, echoed back by Maxio. Null when it was not supplied.</summary>
    public string? Reference { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Price of the subscribed plan, in the smallest unit of <see cref="Currency"/>.</summary>
    public int PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When Maxio will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Outstanding balance on the subscription, in the smallest currency unit.</summary>
    public int BalanceInCents { get; init; }

    /// <summary>How Maxio collects payment, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; init; }

    public bool IsLive => SubscriptionStates.IsLive(State);
}
