namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio "product" within the configured product family), projected into a
/// billing-provider-agnostic shape for the application layer. Money is carried as integer cents,
/// exactly as the provider reports it, so no precision is lost before the boundary formats it.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    int ProductId);
