using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to, projected from the billing system of record.
/// Plans are identified by their <see cref="Handle"/>: handles are stable across catalog
/// re-seeds whereas the numeric <see cref="Id"/> is not, so callers should always
/// round-trip the handle.
/// </summary>
public class SubscriptionPlan
{
    public int? Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Wire value of the billing interval unit (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>
    /// Whether the billing provider requires a payment method before the subscription can be created.
    /// <see langword="null"/> means the provider did not report it - treat that as unknown, not as "no".
    /// </summary>
    public bool? PaymentMethodRequired { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
