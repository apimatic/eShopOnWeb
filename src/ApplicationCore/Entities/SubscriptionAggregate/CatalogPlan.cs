namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record CatalogPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int? Interval,
    string? IntervalUnit);
