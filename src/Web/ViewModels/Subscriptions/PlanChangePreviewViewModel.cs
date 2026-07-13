namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

public class PlanChangePreviewViewModel
{
    public long SubscriptionId { get; set; }
    public string FromProductHandle { get; set; } = string.Empty;
    public string ToProductHandle { get; set; } = string.Empty;
    public decimal ProratedAdjustment { get; set; }
    public long ProratedAdjustmentInCents { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
}
