namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change straight away, prorating the current period.</summary>
    Immediate = 0,

    /// <summary>Defer the change to the start of the next billing period; no proration.</summary>
    AtNextRenewal = 1
}
