namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribe-able plan (a Maxio product) offered within the configured product family.
/// </summary>
public record SubscriptionPlan(
    int ProductId,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);
