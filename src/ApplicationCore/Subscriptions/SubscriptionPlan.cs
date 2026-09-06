namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as reported by the billing system of record.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan in the billing system.</summary>
    public string Handle { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>Number of <see cref="IntervalUnit"/>s between billings (for example 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Unit the billing interval is measured in (for example "month").</summary>
    public string? IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public string? ProductFamilyName { get; init; }

    /// <summary>True when the billing system demands a stored payment method before signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>True when the plan starts with a trial period.</summary>
    public bool HasTrial { get; init; }

    /// <summary>One-off charge applied at signup, in cents. Zero when there is no setup fee.</summary>
    public long SetupFeeInCents { get; init; }
}
