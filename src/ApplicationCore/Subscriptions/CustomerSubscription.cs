using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as the billing system currently reports it.
/// </summary>
public sealed record CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public required long Id { get; init; }

    /// <summary>The idempotency reference eShopOnWeb assigned when creating the subscription.</summary>
    public string? Reference { get; init; }

    /// <summary>Lifecycle state reported by the billing system, e.g. "active", "past_due", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public required bool IsLive { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price currently charged for this subscription.</summary>
    public decimal? Price { get; init; }

    public string? Currency { get; init; }

    public int? IntervalLength { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>When the billing system will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>How payment is collected, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Identifier of the owning customer in the billing system.</summary>
    public required long BillingCustomerId { get; init; }
}
