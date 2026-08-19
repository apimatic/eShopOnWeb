namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio product offered as a recurring subscription plan.
/// </summary>
public sealed record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit,
    string? ProductFamilyHandle);
