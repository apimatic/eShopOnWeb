namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription plan a shopper can enroll in. Backed by a Maxio Advanced Billing
/// product that lives under the configured product family. Handles are stable across
/// re-seeds; numeric ids are not, so callers should prefer <see cref="Handle"/>.
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human friendly price, e.g. <c>$299.00</c>.</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>Number of interval units between renewals (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Interval unit for renewals (e.g. <c>month</c> or <c>day</c>).</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    public string ProductFamilyHandle { get; init; } = string.Empty;
}
