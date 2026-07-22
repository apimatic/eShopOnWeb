namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public string TargetPlanHandle { get; set; }
    public string Timing { get; set; }
    public int ProratedAdjustmentInCents { get; set; }
    public int ChargeInCents { get; set; }
    public int PaymentDueInCents { get; set; }
    public int CreditAppliedInCents { get; set; }
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
}
