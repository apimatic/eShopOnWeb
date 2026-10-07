namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to — a Maxio product in the
/// configured product family.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable Maxio handle for the plan (e.g. "eshop-pro"). Use this value to
    /// subscribe via POST api/subscriptions.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>
    /// Recurring price in the plan's currency.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Recurring price in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    public int Interval { get; set; } = 1;

    /// <summary>
    /// Billing interval unit ("month" or "day").
    /// </summary>
    public string? IntervalUnit { get; set; }

    public string? ProductPricePointHandle { get; set; }

    /// <summary>
    /// Whether the plan requires a payment method at signup.
    /// </summary>
    public bool? RequireCreditCard { get; set; }
}
