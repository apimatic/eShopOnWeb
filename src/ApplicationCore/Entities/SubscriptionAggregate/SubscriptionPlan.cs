namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequireCreditCard);
