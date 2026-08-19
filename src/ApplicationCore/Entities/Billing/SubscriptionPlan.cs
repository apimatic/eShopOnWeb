namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
