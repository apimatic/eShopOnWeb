using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

/// <summary>
/// The prorated cost shown to the customer before they commit to a plan change (UC3 step 2).
/// </summary>
public class PlanChangePreviewViewModel
{
    public long SubscriptionId { get; set; }

    public string CurrentPlanHandle { get; set; } = string.Empty;

    public string TargetPlanHandle { get; set; } = string.Empty;

    public PlanChangeTiming Timing { get; set; }

    public decimal ProratedAdjustment { get; set; }

    public decimal Charge { get; set; }

    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }

    /// <summary>
    /// Round-tripped with the confirmation so the commit is rejected if the provider's numbers
    /// moved after this preview was shown.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;
}
