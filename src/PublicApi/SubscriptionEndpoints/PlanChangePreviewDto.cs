namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;

    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }

    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }

    /// <summary>The amount the customer pays now, in dollars — the figure shown before confirming.</summary>
    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
}
