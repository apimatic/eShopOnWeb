namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionPlanDto(
    string Handle,
    string Name,
    long PriceInCents,
    int IntervalInMonths,
    string? Description
);
