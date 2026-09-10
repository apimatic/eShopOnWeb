namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to, projected from the billing provider's
/// product catalog. Prices are in the plan's own currency, expressed in minor units (cents).
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string? IntervalUnit);
