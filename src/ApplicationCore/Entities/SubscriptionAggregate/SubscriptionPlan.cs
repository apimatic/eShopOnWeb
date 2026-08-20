namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);
