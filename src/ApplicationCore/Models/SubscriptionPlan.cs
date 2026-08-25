namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan (Maxio product) that a shopper can subscribe to.
/// </summary>
public record SubscriptionPlan(
    long Id,
    string Name,
    string? Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
