namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The cost of moving a subscription to a different plan, quoted before the change is committed.
/// All amounts are in the site currency (e.g. 270.00), not in minor units.
/// </summary>
public class BillingPlanChangePreview
{
    public int SubscriptionId { get; set; }
    public string? CurrentProductHandle { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Whether the quote assumes the change is applied now with proration (true) or at renewal (false).
    /// </summary>
    public bool Prorate { get; set; }

    /// <summary>
    /// The credit issued for the unused remainder of the current plan.
    /// </summary>
    public decimal ProratedAdjustment { get; set; }

    /// <summary>
    /// The charge raised for the new plan.
    /// </summary>
    public decimal Charge { get; set; }

    /// <summary>
    /// The net amount the customer owes once adjustment and credit are applied.
    /// </summary>
    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
}
