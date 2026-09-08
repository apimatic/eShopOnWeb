namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscription plan (Maxio product) that a shopper can subscribe to.
/// </summary>
public sealed class SubscriptionPlan
{
    public long Id { get; init; }

    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public long PriceInCents { get; init; }

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = "month";

    public string ProductFamilyName { get; init; } = string.Empty;

    public string ProductFamilyHandle { get; init; } = string.Empty;

    public bool Archived { get; init; }
}
