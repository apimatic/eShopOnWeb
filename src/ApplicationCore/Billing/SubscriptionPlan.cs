namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    string? ProductFamilyHandle);
