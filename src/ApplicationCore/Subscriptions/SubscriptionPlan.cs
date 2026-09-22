using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan available to shoppers. Maps from a Maxio product within the configured
/// product family. The <see cref="Handle"/> is the stable identifier used to subscribe.
/// </summary>
public record SubscriptionPlan
{
    public required string Handle { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (as reported by Maxio).</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Billing interval count (e.g. 1) coupled with <see cref="IntervalUnit"/>.</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit wire value (e.g. "month" or "day").</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Maxio numeric product id. Not stable across re-seeds; the handle is authoritative.</summary>
    public int? ProductId { get; init; }
}
