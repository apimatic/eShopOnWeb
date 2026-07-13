namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public string CurrentProductHandle { get; set; } = string.Empty;
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; }
    public decimal ProratedAdjustmentInCents { get; set; }
    public decimal ChargeInCents { get; set; }
    public decimal PaymentDueInCents { get; set; }
    public decimal CreditAppliedInCents { get; set; }
}
