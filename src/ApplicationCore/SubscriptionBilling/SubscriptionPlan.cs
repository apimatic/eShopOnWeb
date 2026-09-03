namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed record SubscriptionPlan(
    string Handle,
    string? Name,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequireCreditCard,
    string? ProductFamilyHandle);
