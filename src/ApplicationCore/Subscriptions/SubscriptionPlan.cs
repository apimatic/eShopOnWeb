namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. This is a provider-neutral projection of a
/// billing "product" (in Maxio Advanced Billing terms) that belongs to the configured product family.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan (Maxio product handle), e.g. "eshop-pro".</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Display name of the plan, e.g. "Pro Plan".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional plan description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price expressed in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (PriceInCents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of interval units in a billing period (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Provider identifier of the underlying product (numeric, not stable across re-seeds).</summary>
    public long ProductId { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
