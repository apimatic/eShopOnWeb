namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscribable plan, as read from the external billing system's catalog.
/// </summary>
public sealed record SubscriptionPlan(
    int BillingProductId,
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod)
{
    public decimal Price => PriceInCents / 100m;
}
