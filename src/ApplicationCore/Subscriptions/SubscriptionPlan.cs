namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio product within the configured product family), projected to the
/// fields eShopOnWeb needs to render a plan and start a subscription. Prices are integer cents;
/// <see cref="FormattedPrice"/> is a display-ready string.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string? Name,
    string? Description,
    long PriceInCents,
    string FormattedPrice,
    int? IntervalCount,
    string? IntervalUnit,
    bool RequiresPaymentMethod);
