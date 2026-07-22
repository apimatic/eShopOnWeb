using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }

    public string CurrentPlanHandle { get; set; }

    public string TargetPlanHandle { get; set; }

    public string Timing { get; set; }

    /// <summary>All amounts are in whole currency units.</summary>
    public decimal ProratedAdjustment { get; set; }

    public decimal Charge { get; set; }

    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }

    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>
    /// Echo this back when committing the change. The commit is refused if the quote has moved,
    /// so the customer is never charged an amount other than the one they were shown.
    /// </summary>
    public string PreviewToken { get; set; }
}
