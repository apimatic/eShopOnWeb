namespace Microsoft.eShopWeb.ApplicationCore.Models;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
