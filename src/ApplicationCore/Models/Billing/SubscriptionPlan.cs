namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscribable plan as exposed by the billing provider.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    decimal Price,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool PaymentMethodRequired);