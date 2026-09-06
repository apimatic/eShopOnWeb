namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    decimal PricePerMonth,
    string BillingInterval);
