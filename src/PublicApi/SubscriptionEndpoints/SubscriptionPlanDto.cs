namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable identifier for the plan, e.g. "eshop-pro". Pass this to POST /api/subscriptions.
    /// Handles are used rather than numeric ids because the billing provider reassigns ids when a
    /// catalog is re-seeded.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>"day", "month", or "unknown".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Ready-to-render summary of the recurring charge, e.g. "$299.00 per month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>
    /// True when the provider requires a stored payment method before signup. Subscribing to such
    /// a plan through this API is rejected, because it does not capture card details.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Trial summary, or null when the plan has no trial.</summary>
    public string? Trial { get; set; }
}
