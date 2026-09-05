namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscribable plan (a Maxio "Product") available under the configured product family.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int IntervalCount,
    string IntervalUnit,
    bool RequiresPaymentMethod);
