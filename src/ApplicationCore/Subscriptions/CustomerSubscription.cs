using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public sealed record CustomerSubscription
{
    public required int Id { get; init; }

    /// <summary>The reference this application assigned to the subscription; the idempotency anchor.</summary>
    public string? Reference { get; init; }

    /// <summary>Provider subscription state, e.g. "active", "trialing", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>True when the subscription still entitles the shopper to the plan.</summary>
    public required bool IsLive { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public int? PriceInCents { get; init; }

    public string? Currency { get; init; }

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public int? BalanceInCents { get; init; }

    public required BillingCustomer Customer { get; init; }

    public decimal? Price => PriceInCents.HasValue ? decimal.Divide(PriceInCents.Value, 100m) : null;
}

/// <summary>
/// The billing-provider customer that an eShopOnWeb user maps to.
/// </summary>
public sealed record BillingCustomer
{
    public required int Id { get; init; }

    /// <summary>The reference this application assigned to the customer; stable per eShopOnWeb user.</summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
