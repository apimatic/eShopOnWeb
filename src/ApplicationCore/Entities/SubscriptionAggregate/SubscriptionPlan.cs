namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public record SubscriptionPlan(
    string Handle,
    string Name,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresCreditCard);
