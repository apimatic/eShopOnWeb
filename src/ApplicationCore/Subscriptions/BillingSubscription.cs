using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reflected by the billing system of record (Maxio). Plain domain
/// projection of a Maxio subscription.
/// </summary>
public record BillingSubscription
{
    /// <summary>The subscription id in the billing system.</summary>
    public required int Id { get; init; }

    /// <summary>Handle of the subscribed plan/product.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Display name of the subscribed plan/product.</summary>
    public string? PlanName { get; init; }

    /// <summary>Recurring price currently subscribed, in integer cents.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Lifecycle state wire value (e.g. "active", "trialing", "canceled").</summary>
    public string? State { get; init; }

    /// <summary>When the current billing period ends (next regularly scheduled charge).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next payment capture will be attempted (may diverge from period end on retry).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
