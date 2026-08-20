namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    decimal Price,
    int Interval,
    string IntervalUnit);
