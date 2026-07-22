namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change immediately, prorating the difference against the current period.</summary>
    Immediate = 0,

    /// <summary>Apply the change at the start of the next billing period, with no proration.</summary>
    AtNextRenewal = 1
}
