namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public string CurrentPlanHandle { get; set; }
    public string TargetPlanHandle { get; set; }
    public string Timing { get; set; }
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }

    /// <summary>
    /// Echo this back on the commit call. It is what proves the customer confirmed these exact
    /// amounts; a change is refused rather than applied at a different price.
    /// </summary>
    public string Fingerprint { get; set; }
}
