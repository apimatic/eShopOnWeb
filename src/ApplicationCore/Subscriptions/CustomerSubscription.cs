using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription held by a shopper, as reported by the billing provider (the system of record).
/// </summary>
public record CustomerSubscription
{
    public required long Id { get; init; }

    /// <summary>The reference this application assigned to the subscription, if any.</summary>
    public string? Reference { get; init; }

    public required long CustomerId { get; init; }

    /// <summary>The reference this application assigned to the billing customer, if any.</summary>
    public string? CustomerReference { get; init; }

    public string? CustomerEmail { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    public required SubscriptionState State { get; init; }

    /// <summary>The state exactly as reported by the billing provider.</summary>
    public required string StateName { get; init; }

    /// <summary>The recurring price of the subscribed plan, in the smallest unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    public string? Currency { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next charge will be attempted. Diverges from the period end after a failed payment.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public decimal Price => PriceInCents / 100m;
}
