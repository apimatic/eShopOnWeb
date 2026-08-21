namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    int ProductId,
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    int PricePointId,
    string? PricePointHandle,
    string? PricePointName);
