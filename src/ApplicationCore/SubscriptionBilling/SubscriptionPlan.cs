namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
