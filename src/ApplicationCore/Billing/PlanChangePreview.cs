namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The prorated cost of moving a subscription to a different plan, shown to the customer before commit.
/// All amounts are in major currency units (e.g. 12.34 for $12.34).
/// </summary>
public class PlanChangePreview
{
    public int SubscriptionId { get; set; }

    public string? CurrentPlanHandle { get; set; }

    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Net prorated adjustment. Positive is a charge to the customer, negative is a credit.
    /// </summary>
    public decimal ProratedAdjustment { get; set; }

    public decimal Charge { get; set; }

    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
}
