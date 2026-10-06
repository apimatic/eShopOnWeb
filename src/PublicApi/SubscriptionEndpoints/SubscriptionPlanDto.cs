namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan available for subscription.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable handle of the plan; use as the subscribe target.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price for one billing period.
    /// </summary>
    public decimal Price { get; set; }

    public int BillingInterval { get; set; }

    public string BillingIntervalUnit { get; set; } = "month";

    /// <summary>
    /// Whether a payment method must be captured to subscribe.
    /// </summary>
    public bool RequirePaymentMethod { get; set; }
}