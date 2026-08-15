namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a billing-provider product) presented to the shopper. Provider-neutral:
/// carries no SDK types so the API layer never depends on the billing SDK.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between billings (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit as reported by the provider (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Whether the provider requires a payment method to subscribe to this plan.</summary>
    public bool PaymentMethodRequired { get; set; }
}
