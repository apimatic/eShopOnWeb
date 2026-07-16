namespace Microsoft.eShopWeb.Web.ViewModels;

public class PlanChangePreviewViewModel
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; }
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;
    public decimal Charge => ChargeInCents / 100m;
    public decimal PaymentDue => PaymentDueInCents / 100m;
    public decimal CreditApplied => CreditAppliedInCents / 100m;
}
