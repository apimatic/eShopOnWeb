namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan — a Maxio product within the configured product family. <see cref="Handle"/> is the
/// stable identifier used to subscribe; numeric ids are not surfaced as they are reassigned on re-seed.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);
