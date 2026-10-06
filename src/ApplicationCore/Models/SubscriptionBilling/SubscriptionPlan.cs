namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// A subscribable plan (a product in the billing system of record).
/// </summary>
public class SubscriptionPlan
{
    /// <summary>
    /// Stable handle of the plan in the billing system (used as the subscribe target).
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price for one billing period, in the site's currency.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Number of units (days or months) per billing period.
    /// </summary>
    public int BillingInterval { get; set; }

    /// <summary>
    /// Billing period unit: "day" or "month".
    /// </summary>
    public string BillingIntervalUnit { get; set; } = "month";

    /// <summary>
    /// Whether a payment method is required to subscribe to this plan.
    /// </summary>
    public bool RequirePaymentMethod { get; set; }

    public bool Taxable { get; set; }

    /// <summary>
    /// True when the plan is no longer available for new subscriptions.
    /// </summary>
    public bool Archived { get; set; }
}