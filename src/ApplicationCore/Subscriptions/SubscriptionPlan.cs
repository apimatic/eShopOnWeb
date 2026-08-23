namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool PaymentMethodRequired);
