namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionPlan(
    long Id,
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string PricePointName);
