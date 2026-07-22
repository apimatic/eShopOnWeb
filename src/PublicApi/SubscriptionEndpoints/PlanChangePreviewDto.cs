namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The prorated cost of a plan change. All amounts are in major currency units.
/// </summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; }
    public string TargetPlanHandle { get; set; }

    /// <summary>Positive is a charge to the customer, negative is a credit.</summary>
    public decimal ProratedAdjustment { get; set; }

    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
}
