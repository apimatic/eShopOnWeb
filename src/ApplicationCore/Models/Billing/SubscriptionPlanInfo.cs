namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// Read model of a sellable subscription plan offered by the shop.
/// </summary>
public record SubscriptionPlanInfo(
    string Handle,
    string Name,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    string ProductFamilyHandle);