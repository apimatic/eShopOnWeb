using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by Maxio (the system of record).
/// </summary>
public record CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>Subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Stable handle of the subscribed plan (product).</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Display name of the subscribed plan.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price of the plan in cents at the time of subscription.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-friendly formatted price, e.g. "$299.00".</summary>
    public string PriceFormatted => $"${PriceInCents / 100m:0.00}";

    /// <summary>Currency code for the price (e.g. "USD"), when reported by Maxio.</summary>
    public string? Currency { get; init; }

    /// <summary>When the current billing period ends / the next assessment occurs. Null if unbounded.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the subscription was created in Maxio.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
