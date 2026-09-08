namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A purchasable subscription plan, as exposed by the billing system of record.
/// </summary>
public class SubscriptionPlanInfo
{
    /// <summary>
    /// Stable API handle of the plan (used as the subscribe target).
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price of the plan's default price point, in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    public int BillingInterval { get; set; }

    /// <summary>
    /// Billing interval unit, e.g. "month" or "day".
    /// </summary>
    public string BillingIntervalUnit { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
