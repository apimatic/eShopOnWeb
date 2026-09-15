namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
