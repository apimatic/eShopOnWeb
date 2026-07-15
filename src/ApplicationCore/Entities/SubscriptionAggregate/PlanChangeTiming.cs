namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The two supported timings for a UC3 plan change (see plan.md UC3 main success scenario, step 1).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply immediately, prorating the current period.</summary>
    Now,

    /// <summary>Apply at the subscription's next renewal; no proration.</summary>
    AtNextRenewal
}
