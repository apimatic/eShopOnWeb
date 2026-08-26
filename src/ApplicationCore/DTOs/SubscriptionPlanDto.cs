namespace Microsoft.eShopWeb.ApplicationCore.DTOs;

/// <summary>
/// A subscription plan (Maxio product) available for shoppers to subscribe to.
/// </summary>
public record SubscriptionPlanDto(
    int Id,
    string Name,
    string Handle,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
