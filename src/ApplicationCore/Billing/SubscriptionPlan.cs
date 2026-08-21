namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    string ProductHandle,
    string? PricePointHandle,
    string Name,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);
