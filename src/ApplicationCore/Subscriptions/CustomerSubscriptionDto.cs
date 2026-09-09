using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as confirmed by Maxio. SDK-free projection returned to API callers.
/// </summary>
public record CustomerSubscriptionDto
{
    public int SubscriptionId { get; init; }

    /// <summary>The app-supplied subscription reference (idempotency key: <c>{userId}:{planHandle}</c>).</summary>
    public string? Reference { get; init; }

    /// <summary>Maxio subscription state wire value (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public long PriceInCents { get; init; }

    public decimal Price { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>
    /// The next billing date — Maxio's <c>current_period_ends_at</c>, "when the next regularly
    /// scheduled attempted charge will occur".
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }
}
