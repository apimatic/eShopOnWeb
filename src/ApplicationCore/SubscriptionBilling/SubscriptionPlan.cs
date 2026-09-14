using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A billable plan (Maxio product) that a shopper can subscribe to.
/// </summary>
public class SubscriptionPlan
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price => Math.Round(PriceInCents / 100m, 2);
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public bool Archived { get; init; }
}
