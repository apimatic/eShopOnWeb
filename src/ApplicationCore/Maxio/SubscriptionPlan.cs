namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A billing plan (Maxio "product") that shoppers can subscribe to.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
