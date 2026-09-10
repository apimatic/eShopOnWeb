namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription plan a shopper can subscribe to. Maps to a Maxio Product
/// within the configured product family.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    int ProductId);
