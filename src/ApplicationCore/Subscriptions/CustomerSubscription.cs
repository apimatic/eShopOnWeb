using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system.
/// </summary>
public record CustomerSubscription
{
    public required long Id { get; init; }
    public string? Reference { get; init; }
    public required string State { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Recurring price actually billed for this subscription, in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO 4217 code of the billing site's currency, e.g. "USD".</summary>
    public string? Currency { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next charge will be attempted. Null for subscriptions that never renew again.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public long CustomerId { get; init; }
    public string? CustomerReference { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>True while the enrollment still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
