namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3, step 1).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now; the billing period is preserved and a prorated charge or credit is issued.</summary>
    Immediate = 0,

    /// <summary>Apply at the next renewal; no proration, the new price starts with the next period.</summary>
    AtNextRenewal = 1
}
