namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string Description,
    decimal Price,
    int Interval,
    string IntervalUnit);
