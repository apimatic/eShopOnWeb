namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio "product") belonging to the configured product family.
/// </summary>
public record SubscriptionPlan(
    long Id,
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);
