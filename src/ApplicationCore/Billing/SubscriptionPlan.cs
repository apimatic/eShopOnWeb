namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio product offered as a recurring plan in the configured product family.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit,
    string? ProductFamilyHandle,
    bool RequireCreditCard);
