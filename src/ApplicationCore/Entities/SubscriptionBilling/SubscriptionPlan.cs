namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public sealed record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    string Interval);
