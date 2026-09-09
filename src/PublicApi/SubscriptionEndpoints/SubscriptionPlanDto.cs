namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for purchase, sourced from Maxio Advanced Billing.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable API handle of the plan (Maxio product handle). Use this value to subscribe.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price per billing period, in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// Recurring price per billing period, formatted as a decimal currency string (e.g. "299.00").
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// Length of the billing period (with <see cref="IntervalUnit"/>, e.g. 1 month).
    /// </summary>
    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// True when subscribing requires a payment method on file.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
