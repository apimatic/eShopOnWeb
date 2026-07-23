using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The prorated cost of a plan change, as shown to the customer before they confirm.</summary>
public class PlanChangePreviewDto
{
    public string CurrentPlanHandle { get; set; }

    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> or <c>AtNextRenewal</c>.</summary>
    public string Timing { get; set; }

    public decimal ProratedAdjustment { get; set; }

    public decimal Charge { get; set; }

    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }

    /// <summary>
    /// The amount due in minor units. This is the value that must be echoed back on commit, so the
    /// customer is never charged an amount other than the one they were shown.
    /// </summary>
    public long PaymentDueInCents { get; set; }

    public static PlanChangePreviewDto FromPreview(PlanChangePreview preview)
    {
        return new PlanChangePreviewDto
        {
            CurrentPlanHandle = preview.CurrentPlanHandle,
            TargetPlanHandle = preview.TargetPlanHandle,
            Timing = preview.Timing.ToString(),
            ProratedAdjustment = preview.ProratedAdjustment,
            Charge = preview.Charge,
            PaymentDue = preview.PaymentDue,
            CreditApplied = preview.CreditApplied,
            PaymentDueInCents = preview.PaymentDueInCents
        };
    }
}
