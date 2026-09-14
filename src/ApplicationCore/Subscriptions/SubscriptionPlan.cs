namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A sellable recurring plan, as offered by the billing system.
/// </summary>
/// <param name="Handle">Stable plan key. Everything in this capability is driven by handles, never numeric ids.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional marketing description.</param>
/// <param name="Price">Recurring price per billing period, in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO currency code of the billing site; null when the site could not be read.</param>
/// <param name="IntervalCount">Number of <paramref name="IntervalUnit"/>s in one billing period (e.g. 1).</param>
/// <param name="IntervalUnit">Billing period unit as reported by the billing system (e.g. "month").</param>
/// <param name="RequiresPaymentMethod">True when the plan cannot be subscribed to without a payment method.</param>
public record SubscriptionPlan(
    string Handle,
    string? Name,
    string? Description,
    decimal? Price,
    string? Currency,
    int? IntervalCount,
    string? IntervalUnit,
    bool RequiresPaymentMethod);
