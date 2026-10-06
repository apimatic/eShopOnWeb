namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A plan a shopper can subscribe to, as offered by the billing system.
/// </summary>
public record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit)
{
    public decimal Price => PriceInCents / 100m;
}
