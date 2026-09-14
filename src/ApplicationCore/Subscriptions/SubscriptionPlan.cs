namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Provider-agnostic projection of a billing product.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    string FormattedPrice,
    int Interval,
    string IntervalUnit,
    string Currency,
    bool RequiresPaymentMethod);
