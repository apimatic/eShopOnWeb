using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as held by the billing system.
/// </summary>
public sealed record CustomerSubscription
{
    public required int Id { get; init; }

    /// <summary>The idempotency key this subscription was created with, echoed back by the billing system.</summary>
    public string? Reference { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Lifecycle state as published by the billing system (for example <c>active</c> or <c>trialing</c>).</summary>
    public string? State { get; init; }

    /// <summary>True while the subscription is not in a terminal state (cancelled, expired, failed to create).</summary>
    public bool IsActive { get; init; }

    public long? PriceInCents { get; init; }

    public string? Currency { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the billing system will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? TrialEndedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public decimal? Price => PriceInCents is null ? null : decimal.Divide(PriceInCents.Value, 100m);
}
