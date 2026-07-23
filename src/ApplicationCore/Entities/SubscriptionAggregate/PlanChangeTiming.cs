namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect, and therefore whether proration applies (plan.md UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the new plan straight away and prorate the difference against the current period.</summary>
    Immediately = 0,

    /// <summary>Defer the new plan to the start of the next billing period; nothing is prorated.</summary>
    AtNextRenewal = 1
}
