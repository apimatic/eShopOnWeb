using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. Maps to a Maxio product within the
/// configured product family. Identified by its stable <see cref="Handle"/>; the numeric
/// <see cref="ProductId"/> is informational only (Maxio reassigns it on re-seed).
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable product handle (e.g. "eshop-pro"). Use this to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Numeric Maxio product id. Not stable across re-seeds.</summary>
    public int ProductId { get; init; }

    /// <summary>Display name of the plan.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional plan description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price expressed in cents (e.g. 29900 for $299.00).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-friendly formatted price, e.g. "$299.00".</summary>
    public string PriceFormatted => $"${PriceInCents / 100m:0.00}";

    /// <summary>Length of a billing period, in <see cref="IntervalUnit"/> units (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing interval, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Currency code for the price (e.g. "USD"), when reported by Maxio.</summary>
    public string? Currency { get; init; }
}
