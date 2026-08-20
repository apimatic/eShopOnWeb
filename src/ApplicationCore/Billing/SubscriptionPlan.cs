namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A sellable Maxio product (plan) in the configured product family.
/// </summary>
public sealed record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
