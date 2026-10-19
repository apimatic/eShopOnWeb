namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A recurring plan offered by the configured billing product family.</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string? Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit)
{
    public decimal? Price => PriceInCents is long cents ? cents / 100m : null;
}
