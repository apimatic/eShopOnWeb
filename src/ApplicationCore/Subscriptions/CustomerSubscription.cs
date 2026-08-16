using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by the billing system.
/// </summary>
public sealed class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>The subscription state, e.g. "active", "trialing", "canceled".</summary>
    public required string State { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The currently subscribed recurring price, in integer cents.</summary>
    public long ProductPriceInCents { get; init; }

    /// <summary>Human-readable current price, e.g. "$299.00".</summary>
    public required string FormattedPrice { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>
    /// When the next regularly scheduled charge will occur (Maxio current_period_ends_at).
    /// This is the "next billing date" surfaced to the shopper.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>When payment capture will next be attempted (Maxio next_assessment_at).</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? CustomerReference { get; init; }

    public int CustomerId { get; init; }

    public string? Currency { get; init; }

    /// <summary>
    /// True when <see cref="ISubscriptionBillingService.SubscribeAsync"/> returned an existing
    /// subscription instead of creating a new one (idempotent replay of a subscribe request).
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
