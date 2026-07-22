namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect, which also determines whether it is prorated.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now, keeping the current billing period and issuing a prorated charge or credit.</summary>
    Immediately = 0,

    /// <summary>Apply at the next renewal; nothing is prorated and the new price starts next period.</summary>
    AtNextRenewal
}
