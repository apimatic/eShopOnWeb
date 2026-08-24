namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan (a product in the billing system's product family) that a shopper can subscribe to.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
