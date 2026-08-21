namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio product offered as a recurring plan in the configured product family.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);
