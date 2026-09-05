namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A plan a shopper can subscribe to, as defined in Maxio's product catalog.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int IntervalCount,
    string IntervalUnit);
