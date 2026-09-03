namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int? Interval,
    string? IntervalUnit);
