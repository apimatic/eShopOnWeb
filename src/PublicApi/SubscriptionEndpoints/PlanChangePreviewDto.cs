using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What a plan change would cost, before anything is committed.
/// </summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string? CurrentPlanHandle { get; set; }
    public string? CurrentPlanName { get; set; }
    public decimal CurrentPlanPrice { get; set; }
    public string TargetPlanHandle { get; set; }
    public string TargetPlanName { get; set; }
    public decimal TargetPlanPrice { get; set; }
    public string Timing { get; set; }

    /// <summary>Credit for the unused portion of the current plan.</summary>
    public decimal ProratedAdjustment { get; set; }

    /// <summary>Prorated charge for the remainder of the period on the target plan.</summary>
    public decimal ProratedCharge { get; set; }

    public decimal CreditApplied { get; set; }

    /// <summary>
    /// Net amount payable now. Echo this back as <c>confirmedAmountDue</c> when committing: the commit is
    /// rejected if the provider would charge a different amount by then.
    /// </summary>
    public decimal AmountDue { get; set; }

    public DateTimeOffset? EffectiveAt { get; set; }
}
