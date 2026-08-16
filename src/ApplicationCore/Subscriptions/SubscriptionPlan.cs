namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can enroll in. This is the app's
/// billing-system-agnostic view of a Maxio "product" that lives inside a product family.
/// </summary>
public sealed class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier (Maxio product handle). Prefer this over <see cref="Id"/>.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Numeric identifier assigned by the billing system. Not stable across catalog re-seeds.</summary>
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price, in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Number of <see cref="IntervalUnit"/>s between billings (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing cadence unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
