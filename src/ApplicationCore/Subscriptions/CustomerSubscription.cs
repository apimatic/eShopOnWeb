using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it exists in the billing system of record.
/// </summary>
public sealed record CustomerSubscription
{
    public required long Id { get; init; }

    /// <summary>Provider state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public required string State { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public required long PriceInCents { get; init; }

    public required string Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>When the next renewal will be assessed. Null for subscriptions that will not renew.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required long CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    /// <summary>Outstanding balance in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; init; }

    /// <summary><c>automatic</c>, <c>remittance</c> or <c>prepaid</c>.</summary>
    public string? PaymentCollectionMethod { get; init; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public decimal Balance => decimal.Divide(BalanceInCents, 100m);

    public bool IsLive => SubscriptionStates.IsLive(State);
}
