namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan (Maxio product) available for purchase.
/// </summary>
public record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    string ProductFamilyHandle)
{
    public decimal Price => PriceInCents / 100m;
}
