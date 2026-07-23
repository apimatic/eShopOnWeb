namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public SubscriptionPlanDto? CurrentPlan { get; set; }
    public SubscriptionPlanDto? TargetPlan { get; set; }

    /// <summary>"Immediate" (prorated) or "AtNextRenewal" (not prorated).</summary>
    public string Timing { get; set; } = string.Empty;

    public int ProratedAdjustmentInCents { get; set; }
    public int ChargeInCents { get; set; }

    /// <summary>
    /// What the customer owes now, in minor units. Echo this back on commit so a change is never
    /// applied at an amount other than the one that was shown.
    /// </summary>
    public int PaymentDueInCents { get; set; }

    public int CreditAppliedInCents { get; set; }

    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
}
