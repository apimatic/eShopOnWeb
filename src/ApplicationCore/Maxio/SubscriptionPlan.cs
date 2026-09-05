namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public record SubscriptionPlan(
    string Handle,
    string Name,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);
