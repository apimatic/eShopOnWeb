namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered by this application, as read from Maxio Advanced Billing.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// The Maxio product handle — the stable identifier to pass when subscribing.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// The recurring price in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// The recurring price formatted for display, e.g. "$299.00".
    /// </summary>
    public string PriceDisplay { get; set; } = string.Empty;

    /// <summary>
    /// The billing period, e.g. "every 1 month".
    /// </summary>
    public string BillingInterval { get; set; } = string.Empty;

    /// <summary>
    /// Whether Maxio requires a payment method to subscribe to this plan.
    /// </summary>
    public bool RequireCreditCard { get; set; }
}