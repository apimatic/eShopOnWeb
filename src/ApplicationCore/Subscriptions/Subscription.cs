using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper subscription as held by the billing system of record.
/// </summary>
public class Subscription
{
    public long Id { get; init; }

    /// <summary>The idempotency-bearing reference this application assigned to the subscription.</summary>
    public string? Reference { get; init; }

    public SubscriptionState State { get; init; }

    /// <summary>Raw provider state string, preserved so unrecognised states stay diagnosable.</summary>
    public string? RawState { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The recurring amount currently subscribed to, in cents.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the next charge will be attempted. Usually tracks <see cref="CurrentPeriodEndsAt"/>,
    /// but diverges while a failed payment is being retried.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Outstanding balance on the subscription, in cents.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How the provider collects payment, e.g. automatic or remittance.</summary>
    public string? PaymentCollectionMethod { get; init; }

    public long CustomerId { get; init; }

    public string? CustomerReference { get; init; }
}
