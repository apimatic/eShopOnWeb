namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    decimal Price,
    int? Interval,
    string? IntervalUnit);
