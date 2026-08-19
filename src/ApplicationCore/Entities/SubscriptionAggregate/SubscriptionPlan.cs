namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string Description,
    decimal Price,
    int Interval,
    string IntervalUnit);
