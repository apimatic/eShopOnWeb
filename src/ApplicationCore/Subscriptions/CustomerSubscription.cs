using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public int? Id { get; init; }

    /// <summary>The deterministic reference this application assigned when enrolling.</summary>
    public string? Reference { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Subscription price in the smallest currency unit (cents).</summary>
    public long? PriceInCents { get; init; }

    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    public string? Currency { get; init; }

    /// <summary>Raw provider state wire value (e.g. "active", "trialing", "canceled").</summary>
    public string? State { get; init; }

    /// <summary>True when the state entitles the shopper to the plan right now.</summary>
    public bool IsActive { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the next charge is expected. The provider does not return a "next billing" field on the
    /// subscription itself, so this is the current period end, falling back to the next assessment date.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public int? CustomerId { get; init; }
    public string? CustomerReference { get; init; }
}
