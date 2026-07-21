namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
}
