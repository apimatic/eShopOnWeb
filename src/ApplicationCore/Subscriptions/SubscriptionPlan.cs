namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio Advanced Billing product offered as a recurring plan.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit)
{
    public decimal Price => PriceInCents / 100m;
}
